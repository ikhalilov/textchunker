using System.Diagnostics;
using System.Text;

using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;
using TextChunker.Chunking.Semantic;

namespace TextChunker.Chunking;

/// <summary>Aggregated statistics of a run (--stats).</summary>
public sealed class ChunkingStats
{
    public int ChunkCount { get; init; }
    public long MinChunkTokens { get; init; }
    public double AvgChunkTokens { get; init; }
    public long MaxChunkTokens { get; init; }
    public long InputChars { get; init; }
    public long InputBytes { get; init; }
    public long ElapsedMs { get; init; }

    /// <summary>Number of atoms in the semantic plan; null for non-semantic strategies.</summary>
    public int? SemanticAtomCount { get; init; }

    /// <summary>Embedding cache hits; null if the cache was not used.</summary>
    public long? EmbeddingCacheHits { get; init; }

    /// <summary>Embedding cache misses; null if the cache was not used.</summary>
    public long? EmbeddingCacheMisses { get; init; }
}

/// <summary>
/// Chunking service: streams the source lines, feeds them to the strategy,
/// builds <see cref="Chunk"/> (id/seq/source/offsets/sizes/tokenCount/metadata)
/// and writes them through <see cref="IChunkWriter"/>. The last two chunks are held back
/// so that --min-* / --drop-small can be applied to the final chunk.
/// </summary>
public sealed class ChunkingService
{
    private readonly ITokenizer _tokenizer;
    private IChunker _chunker;
    private readonly ChunkingOptions _options;
    private readonly bool _normalized;
    private readonly IReadOnlyDictionary<string, object?> _userMetadata;
    private readonly string? _sourceOverride;
    private readonly ISemanticPlanner? _semanticPlanner;
    private readonly SemanticOptions _semanticOptions;
    private SemanticPlan? _semanticPlan;
    private string? _semanticFallback;
    private bool _streamingWindow;

    public ChunkingService(
        ITokenizer tokenizer,
        IChunker chunker,
        ChunkingOptions options,
        bool normalized = false,
        IReadOnlyDictionary<string, object?>? userMetadata = null,
        ISemanticPlanner? semanticPlanner = null,
        SemanticOptions? semanticOptions = null)
    {
        _tokenizer = tokenizer ?? throw new ArgumentNullException(nameof(tokenizer));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _normalized = normalized;
        _userMetadata = userMetadata ?? new Dictionary<string, object?>();
        _sourceOverride = _userMetadata.TryGetValue("source", out object? value)
            && value is string text
            && !string.IsNullOrWhiteSpace(text)
                ? text
                : null;
        _semanticPlanner = semanticPlanner;
        _semanticOptions = semanticOptions ?? new SemanticOptions();
        ChunkingOptionsValidator.Validate(options);
        _semanticOptions.Validate();
        ValidatePrefixFitsLimit();
    }

    /// <summary>
    /// The prefix alone must not consume the whole limit: otherwise no chunk could
    /// fit within it (exit 2 / HTTP 400).
    /// </summary>
    private void ValidatePrefixFitsLimit()
    {
        if (_options.Unit != SizeUnit.Tokens
            || _options.MaxSize is not { } max
            || string.IsNullOrEmpty(_options.Prefix))
        {
            return;
        }

        int prefixTokens = _tokenizer.CountTokens(_options.Prefix);
        if (prefixTokens >= max)
        {
            throw new ArgumentOutOfRangeException(
                nameof(_options),
                max,
                $"Префикс содержит {prefixTokens} токенов и не оставляет места в лимите {max} токенов.");
        }
    }

    /// <summary>true — the strategy needs a semantic plan and an embedding provider.</summary>
    public bool RequiresPreplanning => _chunker is ISemanticPlanAware;

    /// <summary>
    /// Asynchronous chunking. For the semantic strategy it builds a boundary plan
    /// (document mode: the whole text in memory, limit <see cref="SemanticOptions.MaxBytes"/>),
    /// then uses the shared pipeline. Other strategies work as usual.
    /// </summary>
    public async Task<ChunkingStats> ProcessAsync(
        ITextSource source,
        IChunkWriter writer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);

        if (_chunker is not ISemanticPlanAware aware)
        {
            return Process(source, writer, cancellationToken);
        }

        if (_semanticPlanner is null)
        {
            throw new InvalidOperationException(
                "Для стратегии 'semantic' нужен провайдер эмбеддингов (--embedding-provider).");
        }

        if (_semanticOptions.Fallback == SemanticFallbackMode.Streaming)
        {
            return await ProcessSemanticWindowsAsync(source, writer, cancellationToken).ConfigureAwait(false);
        }

        // Planning (embeddings) is the most expensive part of semantic; it is included in elapsedMs.
        var stopwatch = Stopwatch.StartNew();
        var builder = new StringBuilder();
        bool firstLine = true;
        foreach (var line in source.Lines())
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!firstLine)
            {
                builder.Append('\n');
            }
            firstLine = false;
            builder.Append(line.Content);
        }

        string text = builder.ToString();
        long sizeBytes = Encoding.UTF8.GetByteCount(text);
        if (sizeBytes > _semanticOptions.MaxBytes)
        {
            string message =
                $"Текст {sizeBytes} байт превышает лимит семантического режима {_semanticOptions.MaxBytes} байт.";

            if (_semanticOptions.Fallback == SemanticFallbackMode.Error)
            {
                throw new SemanticLimitExceededException(message);
            }

            _semanticFallback = "recursive";
            _chunker = new RecursiveChunker();
            return Process(new BufferedTextSource(text, source.SourceName), writer, cancellationToken, stopwatch);
        }

        SemanticPlan plan = await _semanticPlanner.PlanAsync(text, _tokenizer, cancellationToken)
            .ConfigureAwait(false);
        _semanticPlan = plan;
        aware.AttachPlan(plan);

        return Process(new BufferedTextSource(text, source.SourceName), writer, cancellationToken, stopwatch);
    }

    private async Task<ChunkingStats> ProcessSemanticWindowsAsync(
        ITextSource source,
        IChunkWriter writer,
        CancellationToken cancellationToken)
    {
        if (_semanticPlanner is null || _chunker is not ISemanticPlanAware aware)
        {
            throw new InvalidOperationException(
                "Streaming-режим доступен только для стратегии 'semantic' с провайдером эмбеддингов.");
        }

        var stopwatch = Stopwatch.StartNew();
        string sourceName = _sourceOverride ?? source.SourceName;
        _semanticFallback = "streaming";
        _streamingWindow = true;

        int windowLimit = _semanticOptions.StreamWindowBytes;
        int overlapLimit = _semanticOptions.StreamOverlapBytes;
        int seq = 1;
        long inputBytes = 0;
        long inputChars = 0;
        int inputLines = 0;
        int atomCount = 0;
        long cacheHits = 0;
        long cacheMisses = 0;
        var emitted = new List<Chunk>();

        var carry = new List<(string Content, long StartChar, long EndChar)>();
        (string Content, long StartChar, long EndChar)? pending = null;
        bool exhausted = false;

        using IEnumerator<(string Content, long StartChar, long EndChar)> enumerator = source.Lines().GetEnumerator();
        while (true)
        {
            var windowLines = new List<(string Content, long StartChar, long EndChar)>();
            long windowBytes = 0;
            int overlapChars = 0;

            foreach ((string content, long start, long end) in carry)
            {
                windowLines.Add((content, start, end));
                windowBytes += Encoding.UTF8.GetByteCount(content) + (windowLines.Count == 1 ? 0 : 1);
                overlapChars += content.Length;
            }
            if (windowLines.Count > 1)
            {
                overlapChars += windowLines.Count - 1;
            }
            carry.Clear();

            while (true)
            {
                (string Content, long StartChar, long EndChar) line;
                bool fromPending = pending is not null;
                if (fromPending)
                {
                    line = pending.GetValueOrDefault();
                    pending = null;
                }
                else if (!exhausted)
                {
                    if (!enumerator.MoveNext())
                    {
                        exhausted = true;
                        break;
                    }

                    line = enumerator.Current;
                    int contentBytes = Encoding.UTF8.GetByteCount(line.Content);
                    inputBytes += contentBytes + (inputLines == 0 ? 0 : 1);
                    inputLines++;
                    if (line.EndChar > inputChars)
                    {
                        inputChars = line.EndChar;
                    }
                }
                else
                {
                    break;
                }

                int lineBytes = Encoding.UTF8.GetByteCount(line.Content) + (windowLines.Count == 0 ? 0 : 1);
                if (!fromPending && windowLines.Count > 0 && windowBytes + lineBytes > windowLimit)
                {
                    pending = line;
                    break;
                }

                windowLines.Add(line);
                windowBytes += lineBytes;
            }

            if (windowLines.Count == 0)
            {
                break;
            }

            long windowStart = windowLines[0].StartChar;
            string text = string.Join('\n', windowLines.Select(line => line.Content));

            SemanticPlan plan = await _semanticPlanner
                .PlanAsync(text, _tokenizer, cancellationToken)
                .ConfigureAwait(false);
            _semanticPlan = plan;
            aware.AttachPlan(plan);
            atomCount += plan.Atoms.Count;
            if (plan.EmbeddingCacheHits is { } hits)
            {
                cacheHits = hits;
            }
            if (plan.EmbeddingCacheMisses is { } misses)
            {
                cacheMisses = misses;
            }

            var windowWriter = new ListChunkWriter();
            Process(new BufferedTextSource(text, sourceName, windowStart), windowWriter, cancellationToken);

            foreach (Chunk original in windowWriter.Chunks)
            {
                Chunk chunk = original;
                long startLocal = (chunk.StartChar ?? windowStart) - windowStart;
                long endLocal = (chunk.EndChar ?? windowStart) - windowStart;

                // A chunk entirely inside the previous window's overlap is a duplicate.
                if (endLocal <= overlapChars)
                {
                    continue;
                }

                // The chunk starts inside the overlap: trim the prefix so that already emitted
                // text is not duplicated and offsets stay monotonic.
                if (startLocal < overlapChars)
                {
                    Chunk? clipped = ClipChunk(chunk, text, windowStart, overlapChars, endLocal);
                    if (clipped is null)
                    {
                        continue;
                    }
                    chunk = clipped;
                }

                Chunk renumbered = CloneWithSequence(chunk, seq++);
                emitted.Add(renumbered);
                writer.Write(renumbered);
            }

            if (exhausted && pending is null)
            {
                break;
            }

            carry = TakeOverlapTail(windowLines, overlapLimit);
            if (carry.Count >= windowLines.Count && carry.Count > 0)
            {
                // The window is small: do not carry it over entirely, otherwise there will be no progress.
                carry.RemoveAt(0);
            }
        }

        stopwatch.Stop();
        _streamingWindow = false;
        long minTokens = long.MaxValue;
        long maxTokens = 0;
        long sumTokens = 0;
        foreach (Chunk chunk in emitted)
        {
            long tokens = chunk.TokenCount ?? 0;
            minTokens = Math.Min(minTokens, tokens);
            maxTokens = Math.Max(maxTokens, tokens);
            sumTokens += tokens;
        }

        return new ChunkingStats
        {
            ChunkCount = emitted.Count,
            MinChunkTokens = emitted.Count == 0 ? 0 : minTokens,
            AvgChunkTokens = emitted.Count == 0 ? 0 : (double)sumTokens / emitted.Count,
            MaxChunkTokens = emitted.Count == 0 ? 0 : maxTokens,
            InputChars = inputChars,
            InputBytes = inputBytes,
            ElapsedMs = stopwatch.ElapsedMilliseconds,
            SemanticAtomCount = atomCount,
            EmbeddingCacheHits = cacheHits == 0 && cacheMisses == 0 ? null : cacheHits,
            EmbeddingCacheMisses = cacheHits == 0 && cacheMisses == 0 ? null : cacheMisses
        };
    }

    private static List<(string Content, long StartChar, long EndChar)> TakeOverlapTail(
        IReadOnlyList<(string Content, long StartChar, long EndChar)> lines,
        int maxBytes)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var tail = new List<(string Content, long StartChar, long EndChar)>();
        if (maxBytes <= 0 || lines.Count == 0)
        {
            return tail;
        }

        int bytes = 0;
        for (int i = lines.Count - 1; i >= 0; i--)
        {
            int size = Encoding.UTF8.GetByteCount(lines[i].Content) + 1;
            if (tail.Count > 0 && bytes + size > maxBytes)
            {
                break;
            }

            tail.Insert(0, lines[i]);
            bytes += size;
        }

        return tail;
    }

    private Chunk? ClipChunk(Chunk chunk, string windowText, long windowStart, int clipStart, long endLocal)
    {
        int start = Math.Max(clipStart, 0);
        int end = (int)Math.Min(endLocal, windowText.Length);
        if (end <= start)
        {
            return null;
        }

        int leading = start;
        while (leading < end && char.IsWhiteSpace(windowText[leading]))
        {
            leading++;
        }
        int trailing = end;
        while (trailing > leading && char.IsWhiteSpace(windowText[trailing - 1]))
        {
            trailing--;
        }
        if (trailing <= leading)
        {
            return null;
        }

        string clipped = windowText.Substring(leading, trailing - leading);
        return new Chunk
        {
            Id = chunk.Id,
            Seq = chunk.Seq,
            Source = chunk.Source,
            Text = clipped,
            StartChar = windowStart + leading,
            EndChar = windowStart + trailing,
            CharCount = clipped.Length,
            ByteCount = Encoding.UTF8.GetByteCount(clipped),
            TokenCount = ChunkTokenCount.ForFinalChunk(_tokenizer, clipped, _options.Prefix),
            Metadata = chunk.Metadata
        };
    }

    private static Chunk CloneWithSequence(Chunk chunk, int seq)
    {
        return new Chunk
        {
            Id = $"{chunk.Source}:{seq:D6}",
            Seq = seq,
            Source = chunk.Source,
            Text = chunk.Text,
            StartChar = chunk.StartChar,
            EndChar = chunk.EndChar,
            CharCount = chunk.CharCount,
            ByteCount = chunk.ByteCount,
            TokenCount = chunk.TokenCount,
            Metadata = chunk.Metadata
        };
    }

    public ChunkingStats Process(ITextSource source, IChunkWriter writer)
    {
        return Process(source, writer, CancellationToken.None);
    }

    public ChunkingStats Process(ITextSource source, IChunkWriter writer, CancellationToken cancellationToken)
    {
        return Process(source, writer, cancellationToken, stopwatch: null);
    }

    private ChunkingStats Process(
        ITextSource source,
        IChunkWriter writer,
        CancellationToken cancellationToken,
        Stopwatch? stopwatch)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(writer);

        Stopwatch clock = stopwatch ?? Stopwatch.StartNew();
        string sourceName = _sourceOverride ?? source.SourceName;
        var measure = new SizeMeasure(_tokenizer, _options.Unit, _options.Prefix);
        var buffer = new StringBuilder();
        var tail = new Queue<Chunk>(2);
        long bufferStart = 0;
        long inputChars = 0;
        long inputBytes = 0;
        int nextSeq = 1;
        int chunkCount = 0;
        long minTokens = long.MaxValue;
        long maxTokens = 0;
        long sumTokens = 0;

        void WriteNow(Chunk chunk)
        {
            writer.Write(chunk);
            int tokens = chunk.TokenCount ?? 0;
            chunkCount++;
            sumTokens += tokens;
            if (tokens < minTokens)
            {
                minTokens = tokens;
            }
            if (tokens > maxTokens)
            {
                maxTokens = tokens;
            }
        }

        void Emit(Chunk chunk)
        {
            tail.Enqueue(chunk);
            if (tail.Count > 2)
            {
                WriteNow(tail.Dequeue());
            }
        }

        void Drain(bool final, CancellationToken token)
        {
            while (buffer.Length > 0)
            {
                token.ThrowIfCancellationRequested();
                string text = buffer.ToString();
                ChunkSplit split = _chunker.Chunk(text, _tokenizer, _options, final);

                foreach (ChunkSegment segment in split.Segments)
                {
                    Emit(BuildChunk(segment, bufferStart, nextSeq));
                    nextSeq++;
                }

                if (split.ConsumedEnd <= 0)
                {
                    return;
                }

                buffer.Remove(0, split.ConsumedEnd);
                bufferStart += split.ConsumedEnd;

                if (!split.MoreNeeded)
                {
                    return;
                }
            }
        }

        bool firstLine = true;
        try
        {
            foreach (var line in source.Lines())
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (firstLine)
                {
                    bufferStart = line.StartChar;
                    firstLine = false;
                }
                else
                {
                    buffer.Append('\n');
                    inputBytes += 1;
                }

                buffer.Append(line.Content);
                inputBytes += Encoding.UTF8.GetByteCount(line.Content);
                if (line.EndChar > inputChars)
                {
                    inputChars = line.EndChar;
                }

                Drain(final: false, cancellationToken);
            }

            Drain(final: true, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Cancellation (Ctrl+C): complete chunks already collected are still emitted.
            // Chunk writing is atomic (the writer writes a whole JSON line), so none are half-written.
            FlushTail();
            throw;
        }

        FlushTail();

        clock.Stop();
        return new ChunkingStats
        {
            ChunkCount = chunkCount,
            MinChunkTokens = chunkCount == 0 ? 0 : minTokens,
            AvgChunkTokens = chunkCount == 0 ? 0 : (double)sumTokens / chunkCount,
            MaxChunkTokens = chunkCount == 0 ? 0 : maxTokens,
            InputChars = inputChars,
            InputBytes = inputBytes,
            ElapsedMs = clock.ElapsedMilliseconds,
            SemanticAtomCount = _semanticPlan?.Atoms.Count,
            EmbeddingCacheHits = _semanticPlan?.EmbeddingCacheHits,
            EmbeddingCacheMisses = _semanticPlan?.EmbeddingCacheMisses
        };

        Chunk BuildChunk(ChunkSegment segment, long absoluteStart, int seq)
        {
            string text = segment.Text;
            return new Chunk
            {
                Id = $"{sourceName}:{seq:D6}",
                Seq = seq,
                Source = sourceName,
                Text = text,
                StartChar = absoluteStart + segment.StartInText,
                EndChar = absoluteStart + segment.EndInText,
                CharCount = text.Length,
                ByteCount = Encoding.UTF8.GetByteCount(text),
                TokenCount = ChunkTokenCount.ForFinalChunk(_tokenizer, text, _options.Prefix),
                Metadata = BuildMetadata(measure)
            };
        }

        bool IsTooSmall(Chunk chunk)
        {
            int? min = _options.Unit switch
            {
                SizeUnit.Tokens => _options.MinTokens,
                SizeUnit.Chars => _options.MinChars,
                SizeUnit.Words => _options.MinWords,
                _ => null
            };
            return !_streamingWindow && min is { } value && measure.Count(chunk.Text) < value;
        }

        void FlushTail()
        {
            if (tail.Count == 0)
            {
                return;
            }

            if (tail.Count == 1)
            {
                Chunk only = tail.Dequeue();
                if (!(IsTooSmall(only) && _options.DropSmall))
                {
                    WriteNow(only);
                }
                return;
            }

            Chunk previous = tail.Dequeue();
            Chunk last = tail.Dequeue();

            if (IsTooSmall(last) && !_options.DropSmall)
            {
                string separator = string.Equals(_options.Strategy, "paragraph", StringComparison.OrdinalIgnoreCase)
                    ? "\n\n"
                    : " ";
                string mergedText = previous.Text + separator + last.Text;
                int max = _options.MaxSize ?? int.MaxValue;
                if (measure.Count(mergedText) <= max)
                {
                    WriteNow(MergeChunks(previous, last, mergedText));
                    return;
                }
            }

            WriteNow(previous);
            if (!(IsTooSmall(last) && _options.DropSmall))
            {
                WriteNow(last);
            }
        }

        Chunk MergeChunks(Chunk previous, Chunk last, string mergedText)
        {
            return new Chunk
            {
                Id = previous.Id,
                Seq = previous.Seq,
                Source = previous.Source,
                Text = mergedText,
                StartChar = previous.StartChar,
                EndChar = last.EndChar,
                CharCount = mergedText.Length,
                ByteCount = Encoding.UTF8.GetByteCount(mergedText),
                TokenCount = ChunkTokenCount.ForFinalChunk(_tokenizer, mergedText, _options.Prefix),
                Metadata = previous.Metadata
            };
        }
    }

    private Dictionary<string, object?> BuildMetadata(SizeMeasure measure)
    {
        var metadata = new Dictionary<string, object?>();
        foreach (KeyValuePair<string, object?> pair in _userMetadata)
        {
            metadata[pair.Key] = pair.Value;
        }

        metadata["strategy"] = _options.Strategy;
        metadata["tokenizer"] = _tokenizer.Name;
        metadata["unit"] = UnitName(_options.Unit);
        metadata["normalized"] = _normalized;

        // Semantic chunking: provider/model/percentile and the fallback applied.
        if (!string.IsNullOrWhiteSpace(_options.Strategy)
            && _options.Strategy.Trim().Equals(ChunkerFactory.SemanticStrategyName, StringComparison.OrdinalIgnoreCase)
            || _semanticPlan is not null
            || _semanticFallback is not null)
        {
            string provider = _semanticPlan?.Provider ?? "none";
            metadata["semanticProvider"] = provider;
            metadata["breakpointPercentile"] = _semanticOptions.Percentile;

            string? model = _semanticPlan?.Model;
            if (!string.IsNullOrWhiteSpace(model))
            {
                metadata["semanticModel"] = model;
            }
        }

        if (_semanticFallback is not null)
        {
            metadata["semanticFallback"] = _semanticFallback;
        }

        if (_options.MaxSize is { } maxSize)
        {
            metadata["maxSize"] = maxSize;
        }

        if (_tokenizer.ModelName is { Length: > 0 } modelName)
        {
            metadata["tokenizerModel"] = modelName;
        }

        if (!string.IsNullOrEmpty(_options.Prefix))
        {
            metadata["prefix"] = _options.Prefix;
            metadata["prefixTokens"] = measure.PrefixTokens;
        }

        return metadata;
    }

    private static string UnitName(SizeUnit unit)
    {
        return unit switch
        {
            SizeUnit.Tokens => "tokens",
            SizeUnit.Chars => "chars",
            SizeUnit.Words => "words",
            SizeUnit.Bytes => "bytes",
            _ => "tokens"
        };
    }
}
