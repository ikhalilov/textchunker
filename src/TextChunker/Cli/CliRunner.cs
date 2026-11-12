using System.CommandLine;
using System.Reflection;
using System.Text;
using System.Text.Json;

using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;
using TextChunker.Chunking;
using TextChunker.Chunking.Semantic;
using TextChunker.Embeddings;
using TextChunker.Sources;
using TextChunker.Tokenizers;
using TextChunker.Writers;

namespace TextChunker.Cli;

/// <summary>Argument parsing and pipeline invocation: source → tokenizer → chunking → writer.</summary>
public static class CliRunner
{
    public static int Run(string[] args)
    {
        return Run(args, CancellationToken.None);
    }

    /// <summary>
    /// Testable entry point: the external <paramref name="cancellationToken"/> simulates Ctrl+C,
    /// and <paramref name="configureConsole"/> keeps tests from touching the global Console streams.
    /// </summary>
    public static int Run(string[] args, CancellationToken cancellationToken, bool configureConsole = true)
    {
        if (configureConsole)
        {
            ConfigureConsole();
        }

        if (args.Length == 1 && string.Equals(args[0], "--version", StringComparison.Ordinal))
        {
            WriteVersion();
            return 0;
        }

        var options = new Options();
        var root = new RootCommand("TextChunker — streaming text chunking for RAG.");
        options.AddTo(root);
        root.SetAction(parseResult => Execute(parseResult, options, cancellationToken));

        ParseResult parsed = root.Parse(args);
        if (parsed.Errors.Count > 0)
        {
            foreach (var error in parsed.Errors)
            {
                Console.Error.WriteLine($"Ошибка аргументов: {error.Message}");
            }
            return 2;
        }

        var invocation = new InvocationConfiguration
        {
            Output = Console.Out,
            Error = Console.Error
        };
        int exitCode = parsed.Invoke(invocation);
        Console.Out.Flush();
        Console.Error.Flush();
        return exitCode;
    }

    private static void WriteVersion()
    {
        Assembly assembly = typeof(CliRunner).Assembly;
        string version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";

        Console.Out.WriteLine(version);
        Console.Out.Flush();
    }

    /// <summary>
    /// Console setup:
    /// 1) when stdout/stderr are redirected (pipe/file), .NET by default uses
    ///    the console code page and replaces non-ASCII with '?'. We swap the writers for UTF-8
    ///    without a BOM (JSONL and messages stay correct);
    /// 2) when an interactive console/debugger window is present, we switch the code page
    ///    to UTF-8; otherwise Cyrillic in diagnostics turns into '?'.
    /// </summary>
    private static void ConfigureConsole()
    {
        var utf8 = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        try
        {
            if (Console.IsOutputRedirected)
            {
                Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), utf8)
                {
                    AutoFlush = false
                });
            }

            if (Console.IsErrorRedirected)
            {
                Console.SetError(new StreamWriter(Console.OpenStandardError(), utf8)
                {
                    AutoFlush = true
                });
            }

            if (!Console.IsOutputRedirected || !Console.IsErrorRedirected)
            {
                try
                {
                    Console.OutputEncoding = utf8;
                    Console.InputEncoding = utf8;
                }
                catch (Exception)
                {
                    // The console is unavailable (for example, no window) — leave it as is.
                }
            }
        }
        catch (IOException)
        {
            // No access to the standard streams — keep the default behavior.
        }
    }

    private static int Execute(ParseResult parseResult, Options options, CancellationToken cancellationToken)
    {
        try
        {
            RunConfig config = options.Read(parseResult);
            if (config.PruneCache)
            {
                return PruneCache(config);
            }
            return RunPipeline(config, cancellationToken);
        }
        catch (TextSourceErrorException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (EmbeddingProviderException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (SemanticLimitExceededException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return ex.ExitCode;
        }
        catch (TokenizerLoadException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 5;
        }
        catch (ArgumentOutOfRangeException ex)
        {
            Console.Error.WriteLine($"Ошибка аргументов: {ex.Message}");
            return 2;
        }
        catch (ArgumentException ex)
        {
            Console.Error.WriteLine($"Ошибка аргументов: {ex.Message}");
            return 2;
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.Error.WriteLine($"Ошибка записи: {ex.Message}");
            return 6;
        }
        catch (IOException ex)
        {
            Console.Error.WriteLine($"Ошибка ввода-вывода: {ex.Message}");
            return 6;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Ошибка: {ex.Message}");
            return 1;
        }
    }

    private static int RunPipeline(RunConfig config, CancellationToken externalToken)
    {
        ChunkingOptions chunkingOptions = config.ToChunkingOptions();
        ChunkingOptionsValidator.Validate(chunkingOptions);

        ITextSource source = CreateSource(config);
        TextWriter? output = null;
        try
        {
            ITokenizer tokenizer = CreateTokenizer(config);
            output = CreateOutput(config);
            IChunkWriter writer = CreateWriter(config.Format, output);

            var service = new ChunkingService(
                tokenizer,
                ChunkerFactory.Create(config.Strategy),
                chunkingOptions,
                normalized: config.HasNormalization,
                userMetadata: config.UserMetadata,
                semanticPlanner: config.CreateSemanticPlanner(),
                semanticOptions: config.Semantic);

            if (config.SemanticFallbackWarning is { Length: > 0 } warning)
            {
                Console.Error.WriteLine(warning);
            }

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(externalToken);
            ConsoleCancelEventHandler onCancel = (_, eventArgs) =>
            {
                eventArgs.Cancel = true;
                cancellation.Cancel();
            };
            Console.CancelKeyPress += onCancel;

            ChunkingStats stats;
            try
            {
                stats = service.ProcessAsync(source, writer, cancellation.Token).GetAwaiter().GetResult();
            }
            catch (OperationCanceledException)
            {
                output.Flush();
                Console.Error.WriteLine("Обработка прервана; выведены только завершённые чанки.");
                return 0;
            }
            finally
            {
                Console.CancelKeyPress -= onCancel;
            }

            output.Flush();

            if (config.Stats)
            {
                WriteStats(stats);
            }

            return 0;
        }
        finally
        {
            (source as IDisposable)?.Dispose();
            if (output is not null && !ReferenceEquals(output, Console.Out))
            {
                output.Dispose();
            }
        }
    }

    private static ITextSource CreateSource(RunConfig config)
    {
        var sourceOptions = new TextSourceOptions
        {
            EncodingName = config.EncodingName,
            InvalidAsError = config.InvalidAsError,
            NormalizeNfc = config.NormalizeNfc,
            NormalizeNfkc = config.NormalizeNfkc,
            NormalizeLineEndings = config.NormalizeLineEndings,
            NormalizeWhitespace = config.NormalizeWhitespace
        };

        if (string.IsNullOrEmpty(config.InputPath) || config.InputPath == "-")
        {
            return new StdinTextSource(sourceOptions);
        }

        return new FileTextSource(config.InputPath, sourceOptions);
    }

    private static ITokenizer CreateTokenizer(RunConfig config)
    {
        if (string.IsNullOrWhiteSpace(config.TokenizerName))
        {
            Console.Error.WriteLine(
                "warning: --tokenizer не указан — используется приближённый word-based подсчёт; " +
                "для точного лимита укажите --tokenizer huggingface --tokenizer-model google/embeddinggemma-2.");
        }

        return TokenizerFactory.Create(
            config.TokenizerName,
            config.TokenizerPath ?? config.TokenizerModel,
            config.TokenizerCacheDir);
    }

    private static TextWriter CreateOutput(RunConfig config)
    {
        if (string.IsNullOrEmpty(config.OutputPath))
        {
            return Console.Out;
        }

        return new StreamWriter(
            config.OutputPath,
            append: false,
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static IChunkWriter CreateWriter(string format, TextWriter output)
    {
        return format switch
        {
            "text" => new TextChunkWriter(output),
            _ => new JsonLinesWriter(output)
        };
    }

    private static int PruneCache(RunConfig config)
    {
        string root = EmbeddingCache.ResolveRoot(config.Embedding.CacheDir);
        TimeSpan? ttl = config.Embedding.CacheTtlDays is { } days
            ? TimeSpan.FromDays(days)
            : null;

        try
        {
            CachePruneResult result = EmbeddingCachePruner.Prune(
                root,
                config.Embedding.CacheMaxBytes,
                ttl,
                config.PruneCacheDryRun);

            var payload = new Dictionary<string, object?>
            {
                ["cacheRoot"] = result.Root,
                ["scanned"] = result.Scanned,
                ["deleted"] = result.Deleted,
                ["tempDeleted"] = result.TempDeleted,
                ["failed"] = result.Failed,
                ["bytesBefore"] = result.BytesBefore,
                ["bytesAfter"] = result.BytesAfter,
                ["bytesFreed"] = result.BytesFreed,
                ["dryRun"] = result.DryRun
            };
            Console.Out.WriteLine(JsonSerializer.Serialize(payload));
            Console.Out.Flush();
            return 0;
        }
        catch (Exception exception) when (
            exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Console.Error.WriteLine($"Ошибка обслуживания кэша: {exception.Message}");
            return 1;
        }
    }

    private static void WriteStats(ChunkingStats stats)
    {
        var payload = new Dictionary<string, object?>
        {
            ["chunkCount"] = stats.ChunkCount,
            ["minChunkTokens"] = stats.MinChunkTokens,
            ["avgChunkTokens"] = Math.Round(stats.AvgChunkTokens, 2),
            ["maxChunkTokens"] = stats.MaxChunkTokens,
            ["inputBytes"] = stats.InputBytes,
            ["inputChars"] = stats.InputChars,
            ["elapsedMs"] = stats.ElapsedMs
        };

        // Semantic diagnostics — only when present.
        if (stats.SemanticAtomCount is { } atomCount)
        {
            payload["semanticAtomCount"] = atomCount;
        }
        if (stats.EmbeddingCacheHits is { } hits)
        {
            payload["embeddingCacheHits"] = hits;
        }
        if (stats.EmbeddingCacheMisses is { } misses)
        {
            payload["embeddingCacheMisses"] = misses;
        }

        Console.Error.WriteLine(JsonSerializer.Serialize(payload));
    }

    private sealed class RunConfig
    {
        public string? InputPath { get; init; }
        public string? OutputPath { get; init; }
        public string Strategy { get; init; } = "recursive";
        public string? TokenizerName { get; init; }
        public string? TokenizerModel { get; init; }
        public string? TokenizerPath { get; init; }
        public string? TokenizerCacheDir { get; init; }
        public string? Prefix { get; init; }

        public int? MaxTokens { get; init; }
        public int? MaxChars { get; init; }
        public int? MaxWords { get; init; }
        public int? MaxBytes { get; init; }
        public int? MinTokens { get; init; }
        public int? MinChars { get; init; }
        public int? MinWords { get; init; }
        public int? OverlapTokens { get; init; }
        public int? OverlapChars { get; init; }

        public bool Trim { get; init; } = true;
        public bool DropSmall { get; init; }
        public IReadOnlyList<string>? Separators { get; init; }

        public bool Stats { get; init; }
        public string? EncodingName { get; init; } = "utf-8";
        public bool InvalidAsError { get; init; }
        public string Format { get; init; } = "jsonl";

        public bool NormalizeNfc { get; init; }
        public bool NormalizeNfkc { get; init; }
        public bool NormalizeLineEndings { get; init; }
        public bool NormalizeWhitespace { get; init; }

        public IReadOnlyDictionary<string, object?> UserMetadata { get; init; } = new Dictionary<string, object?>();

        public EmbeddingOptions Embedding { get; init; } = new();
        public bool PruneCache { get; init; }
        public bool PruneCacheDryRun { get; init; }
        public SemanticOptions Semantic { get; init; } = new();

        public bool HasNormalization =>
            NormalizeNfc || NormalizeNfkc || NormalizeLineEndings || NormalizeWhitespace;

        /// <summary>Warning that the document exceeds the semantic mode limit.</summary>
        public string? SemanticFallbackWarning { get; init; }

        /// <summary>Semantic boundary planner; null for non-semantic strategies.</summary>
        public ISemanticPlanner? CreateSemanticPlanner()
        {
            if (!ChunkerFactory.RequiresEmbeddings(Strategy))
            {
                return null;
            }

            IEmbeddingProvider provider = EmbeddingProviderFactory.Create(Embedding);
            return new SemanticPlanner(provider, Semantic);
        }

        public ChunkingOptions ToChunkingOptions()
        {
            int? maxTokens = MaxTokens;
            int? maxChars = MaxChars;
            int? maxWords = MaxWords;
            int? maxBytes = MaxBytes;

            if (maxTokens is null && maxChars is null && maxWords is null && maxBytes is null)
            {
                maxTokens = 512; // default RAG limit (see design.md, decision 2)
            }

            return new ChunkingOptions(
                strategy: Strategy,
                maxTokens: maxTokens,
                maxChars: maxChars,
                maxWords: maxWords,
                maxBytes: maxBytes,
                minTokens: MinTokens,
                minChars: MinChars,
                minWords: MinWords,
                overlapTokens: OverlapTokens,
                overlapChars: OverlapChars,
                trim: Trim,
                dropSmall: DropSmall,
                prefix: Prefix,
                separators: Separators);
        }
    }

    private sealed class Options
    {
        private readonly Option<string> _input = new("--input")
        {
            Description = "Input file; '-' or omitted — stdin."
        };

        private readonly Option<string> _output = new("--output")
        {
            Description = "Output file (stdout by default)."
        };

        private readonly Option<string> _strategy = new("--strategy")
        {
            Description = "Strategy: fixed | recursive | paragraph (default recursive)."
        };

        private readonly Option<string> _tokenizer = new("--tokenizer")
        {
            Description = "Tokenizer: none | char | word | regex | huggingface (default word)."
        };

        private readonly Option<string> _tokenizerModel = new("--tokenizer-model")
        {
            Description = "Model/path to tokenizer.json (default google/embeddinggemma-2)."
        };

        private readonly Option<string> _tokenizerPath = new("--tokenizer-path")
        {
            Description = "Path to a local tokenizer.json (takes precedence over --tokenizer-model)."
        };

        private readonly Option<string> _tokenizerCacheDir = new("--tokenizer-cache-dir")
        {
            Description = "tokenizer.json cache directory."
        };

        private readonly Option<string> _prefix = new("--prefix")
        {
            Description = "Task prefix counted toward the token limit."
        };

        private readonly Option<int?> _maxTokens = new("--max-tokens") { Description = "Maximum tokens per chunk." };
        private readonly Option<int?> _maxChars = new("--max-chars") { Description = "Maximum characters per chunk." };
        private readonly Option<int?> _maxWords = new("--max-words") { Description = "Maximum words per chunk." };
        private readonly Option<int?> _maxBytes = new("--max-bytes") { Description = "Maximum UTF-8 bytes per chunk." };
        private readonly Option<int?> _minTokens = new("--min-tokens") { Description = "Minimum tokens for the final chunk." };
        private readonly Option<int?> _minChars = new("--min-chars") { Description = "Minimum characters for the final chunk." };
        private readonly Option<int?> _minWords = new("--min-words") { Description = "Minimum words for the final chunk." };
        private readonly Option<int?> _overlapTokens = new("--overlap-tokens") { Description = "Overlap in tokens." };
        private readonly Option<int?> _overlapChars = new("--overlap-chars") { Description = "Overlap in characters." };

        private readonly Option<bool> _trim = new("--trim") { Description = "Trim whitespace at the edges (enabled by default; --no-trim disables it)." };
        private readonly Option<bool> _noTrim = new("--no-trim") { Description = "Do not trim whitespace at the chunk edges." };
        private readonly Option<bool> _dropSmall = new("--drop-small") { Description = "Discard the final chunk if it is smaller than --min-*." };

        private readonly Option<string[]> _separators = new("--separators")
        {
            Description = "Recursive separators in descending priority order (\"\\n\\n\", \"\\n\", ...).",
            AllowMultipleArgumentsPerToken = true
        };

        private readonly Option<bool> _stats = new("--stats") { Description = "Write statistics (JSON) to stderr." };

        // --- semantic chunking ---
        private readonly Option<string> _embeddingProvider = new("--embedding-provider")
        {
            Description = "Embedding provider: llama-cpp | hash (default llama-cpp)."
        };

        private readonly Option<string> _embeddingEndpoint = new("--embedding-endpoint")
        {
            Description = "URL of the embedding server (default http://127.0.0.1:9932)."
        };

        private readonly Option<string> _embeddingModel = new("--embedding-model")
        {
            Description = "Model name in requests to the embedding server."
        };

        private readonly Option<int?> _embeddingBatchSize = new("--embedding-batch-size")
        {
            Description = "Embedding batch size (default 32)."
        };

        private readonly Option<int?> _embeddingDimensions = new("--embedding-dimensions")
        {
            Description = "Expected vector dimensionality, 0 — do not check (default 768)."
        };

        private readonly Option<int?> _embeddingMaxBatchChars = new("--embedding-max-batch-chars")
        {
            Description = "Character budget per batch (default 8000; 0 — no limit)."
        };

        private readonly Option<int?> _embeddingConcurrency = new("--embedding-concurrency")
        {
            Description = "How many batches to send in parallel (default 1)."
        };

        private readonly Option<int?> _embeddingTimeout = new("--embedding-timeout")
        {
            Description = "HTTP request timeout to the embedding server, seconds (default 120)."
        };

        private readonly Option<int?> _embeddingMaxRetries = new("--embedding-max-retries")
        {
            Description = "Retries on network errors and 429/5xx (default 3)."
        };

        private readonly Option<string> _embeddingPrompt = new("--embedding-prompt")
        {
            Description = "Prefix prompt added to each atom before embedding."
        };

        private readonly Option<string> _embeddingCacheDir = new("--embedding-cache-dir")
        {
            Description = "Embedding cache directory (default LocalApplicationData/TextChunker/embeddings)."
        };

        private readonly Option<long?> _embeddingCacheMaxBytes = new("--embedding-cache-max-bytes")
        {
            Description = "Maximum embedding cache size in bytes (unlimited by default)."
        };

        private readonly Option<int?> _embeddingCacheTtlDays = new("--embedding-cache-ttl-days")
        {
            Description = "Delete cache entries older than N days (no TTL by default)."
        };

        private readonly Option<bool> _pruneCache = new("--prune-cache")
        {
            Description = "Maintain the cache (TTL/limit) and exit, without reading input."
        };

        private readonly Option<bool> _pruneCacheDryRun = new("--prune-cache-dry-run")
        {
            Description = "Show what would be deleted without deleting anything."
        };

        private readonly Option<bool> _noEmbeddingCache = new("--no-embedding-cache")
        {
            Description = "Do not use the on-disk embedding cache."
        };

        private readonly Option<double?> _semanticPercentile = new("--semantic-percentile")
        {
            Description = "Distance percentile for boundaries, (0, 1] (default 0.90)."
        };

        private readonly Option<double?> _semanticMinDistance = new("--semantic-min-distance")
        {
            Description = "Minimum boundary distance (default 0.02)."
        };

        private readonly Option<int?> _semanticWindow = new("--semantic-window")
        {
            Description = "TextTiling window for distances (default 1 — neighboring atoms)."
        };

        private readonly Option<long?> _semanticMaxBytes = new("--semantic-max-bytes")
        {
            Description = "Input size limit for semantic, bytes (default 20000000)."
        };

        private readonly Option<int?> _semanticStreamWindowBytes = new("--semantic-stream-window-bytes")
        {
            Description = "Streaming mode window size, bytes (default 4000000)."
        };

        private readonly Option<int?> _semanticStreamOverlapBytes = new("--semantic-stream-overlap-bytes")
        {
            Description = "Overlap between adjacent streaming windows, bytes (default 256000)."
        };

        private readonly Option<string> _semanticFallback = new("--semantic-fallback")
        {
            Description = "Behavior when the limit is exceeded: recursive | error (default recursive)."
        };

        private readonly Option<int?> _semanticMinAtomChars = new("--semantic-min-atom-chars")
        {
            Description = "Merge atoms shorter than N characters (default 20)."
        };

        private readonly Option<int?> _semanticMaxAtomTokens = new("--semantic-max-atom-tokens")
        {
            Description = "Pre-split atoms longer than N tokens (default 512)."
        };

        private readonly Option<string> _encoding = new("--encoding") { Description = "Input encoding (default utf-8)." };
        private readonly Option<string> _invalidEncoding = new("--invalid-encoding") { Description = "replace | error." };
        private readonly Option<string> _format = new("--format") { Description = "jsonl | text (default jsonl)." };

        private readonly Option<string> _normalize = new("--normalize")
        {
            Description = "Normalization: nfc, nfkc, line-endings, whitespace (comma-separated or all)."
        };

        private readonly Option<string[]> _metadata = new("--metadata")
        {
            Description = "User metadata key=value (can be repeated).",
            AllowMultipleArgumentsPerToken = true
        };

        public void AddTo(RootCommand root)
        {
            root.Add(_input);
            root.Add(_output);
            root.Add(_strategy);
            root.Add(_tokenizer);
            root.Add(_tokenizerModel);
            root.Add(_tokenizerPath);
            root.Add(_tokenizerCacheDir);
            root.Add(_prefix);
            root.Add(_maxTokens);
            root.Add(_maxChars);
            root.Add(_maxWords);
            root.Add(_maxBytes);
            root.Add(_minTokens);
            root.Add(_minChars);
            root.Add(_minWords);
            root.Add(_overlapTokens);
            root.Add(_overlapChars);
            root.Add(_trim);
            root.Add(_noTrim);
            root.Add(_dropSmall);
            root.Add(_separators);
            root.Add(_stats);
            root.Add(_encoding);
            root.Add(_invalidEncoding);
            root.Add(_format);
            root.Add(_normalize);
            root.Add(_metadata);
            root.Add(_embeddingProvider);
            root.Add(_embeddingEndpoint);
            root.Add(_embeddingModel);
            root.Add(_embeddingBatchSize);
            root.Add(_embeddingDimensions);
            root.Add(_embeddingMaxBatchChars);
            root.Add(_embeddingConcurrency);
            root.Add(_embeddingTimeout);
            root.Add(_embeddingMaxRetries);
            root.Add(_embeddingPrompt);
            root.Add(_embeddingCacheDir);
            root.Add(_embeddingCacheMaxBytes);
            root.Add(_embeddingCacheTtlDays);
            root.Add(_pruneCache);
            root.Add(_pruneCacheDryRun);
            root.Add(_noEmbeddingCache);
            root.Add(_semanticPercentile);
            root.Add(_semanticMinDistance);
            root.Add(_semanticWindow);
            root.Add(_semanticMaxBytes);
            root.Add(_semanticStreamWindowBytes);
            root.Add(_semanticStreamOverlapBytes);
            root.Add(_semanticFallback);
            root.Add(_semanticMinAtomChars);
            root.Add(_semanticMaxAtomTokens);
        }

        public RunConfig Read(ParseResult parseResult)
        {
            string? invalidEncoding = Normalize(parseResult.GetValue(_invalidEncoding));
            bool invalidAsError = invalidEncoding switch
            {
                null or "replace" => false,
                "error" => true,
                _ => throw new ArgumentException(
                    $"Неизвестный режим --invalid-encoding '{invalidEncoding}'. Допустимые: replace, error.")
            };

            string? format = Normalize(parseResult.GetValue(_format)) ?? "jsonl";
            if (format is not ("jsonl" or "text"))
            {
                throw new ArgumentException($"Неизвестный формат '{format}'. Допустимые: jsonl, text.");
            }

            var normalization = ParseNormalization(parseResult.GetValue(_normalize));
            string? encoding = parseResult.GetValue(_encoding);

            string strategy = Normalize(parseResult.GetValue(_strategy)) ?? "recursive";

            var embedding = new EmbeddingOptions
            {
                Provider = Normalize(parseResult.GetValue(_embeddingProvider))
                    ?? EmbeddingProviderFactory.DefaultProviderName,
                Endpoint = EmptyToNull(parseResult.GetValue(_embeddingEndpoint))
                    ?? new EmbeddingOptions().Endpoint,
                Model = EmptyToNull(parseResult.GetValue(_embeddingModel)),
                BatchSize = parseResult.GetValue(_embeddingBatchSize) ?? new EmbeddingOptions().BatchSize,
                Dimensions = parseResult.GetValue(_embeddingDimensions) ?? new EmbeddingOptions().Dimensions,
                MaxBatchChars = parseResult.GetValue(_embeddingMaxBatchChars) ?? new EmbeddingOptions().MaxBatchChars,
                Concurrency = parseResult.GetValue(_embeddingConcurrency) ?? new EmbeddingOptions().Concurrency,
                Timeout = parseResult.GetValue(_embeddingTimeout) is { } timeoutSeconds
                    ? TimeSpan.FromSeconds(timeoutSeconds)
                    : new EmbeddingOptions().Timeout,
                MaxRetries = parseResult.GetValue(_embeddingMaxRetries) ?? new EmbeddingOptions().MaxRetries,
                Prompt = EmptyToNull(parseResult.GetValue(_embeddingPrompt)),
                CacheDir = EmptyToNull(parseResult.GetValue(_embeddingCacheDir)),
                CacheMaxBytes = parseResult.GetValue(_embeddingCacheMaxBytes),
                CacheTtlDays = parseResult.GetValue(_embeddingCacheTtlDays),
                UseCache = !parseResult.GetValue(_noEmbeddingCache)
            };

            var defaults = new SemanticOptions();
            var semantic = new SemanticOptions
            {
                Percentile = parseResult.GetValue(_semanticPercentile) ?? defaults.Percentile,
                MinDistance = parseResult.GetValue(_semanticMinDistance) ?? defaults.MinDistance,
                Window = parseResult.GetValue(_semanticWindow) ?? defaults.Window,
                MaxBytes = parseResult.GetValue(_semanticMaxBytes) ?? defaults.MaxBytes,
                StreamWindowBytes = parseResult.GetValue(_semanticStreamWindowBytes) ?? defaults.StreamWindowBytes,
                StreamOverlapBytes = parseResult.GetValue(_semanticStreamOverlapBytes) ?? defaults.StreamOverlapBytes,
                Fallback = SemanticOptions.ParseFallback(parseResult.GetValue(_semanticFallback)),
                MinAtomChars = parseResult.GetValue(_semanticMinAtomChars) ?? defaults.MinAtomChars,
                MaxAtomTokens = parseResult.GetValue(_semanticMaxAtomTokens) ?? defaults.MaxAtomTokens
            };

            semantic.Validate();
            if (ChunkerFactory.RequiresEmbeddings(strategy) || parseResult.GetValue(_pruneCache))
            {
                embedding.Validate();
            }

            return new RunConfig
            {
                InputPath = parseResult.GetValue(_input),
                OutputPath = parseResult.GetValue(_output),
                Strategy = strategy,
                TokenizerName = EmptyToNull(parseResult.GetValue(_tokenizer)),
                TokenizerModel = EmptyToNull(parseResult.GetValue(_tokenizerModel)),
                TokenizerPath = EmptyToNull(parseResult.GetValue(_tokenizerPath)),
                TokenizerCacheDir = EmptyToNull(parseResult.GetValue(_tokenizerCacheDir)),
                Prefix = parseResult.GetValue(_prefix),
                MaxTokens = parseResult.GetValue(_maxTokens),
                MaxChars = parseResult.GetValue(_maxChars),
                MaxWords = parseResult.GetValue(_maxWords),
                MaxBytes = parseResult.GetValue(_maxBytes),
                MinTokens = parseResult.GetValue(_minTokens),
                MinChars = parseResult.GetValue(_minChars),
                MinWords = parseResult.GetValue(_minWords),
                OverlapTokens = parseResult.GetValue(_overlapTokens),
                OverlapChars = parseResult.GetValue(_overlapChars),
                Trim = !parseResult.GetValue(_noTrim),
                DropSmall = parseResult.GetValue(_dropSmall),
                Separators = ParseSeparators(parseResult.GetValue(_separators)),
                Stats = parseResult.GetValue(_stats),
                EncodingName = string.IsNullOrWhiteSpace(encoding) ? "utf-8" : encoding,
                InvalidAsError = invalidAsError,
                Format = format,
                NormalizeNfc = normalization.Nfc,
                NormalizeNfkc = normalization.Nfkc,
                NormalizeLineEndings = normalization.LineEndings,
                NormalizeWhitespace = normalization.Whitespace,
                UserMetadata = ParseMetadata(parseResult.GetValue(_metadata)),
                Embedding = embedding,
                Semantic = semantic,
                PruneCache = parseResult.GetValue(_pruneCache),
                PruneCacheDryRun = parseResult.GetValue(_pruneCacheDryRun)
            };
        }

        private static string? Normalize(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();
        }

        private static string? EmptyToNull(string? value)
        {
            return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        }

        private static IReadOnlyList<string>? ParseSeparators(string[]? values)
        {
            if (values is null || values.Length == 0)
            {
                return null;
            }

            var separators = new List<string>(values.Length);
            foreach (string value in values)
            {
                string item = value.StartsWith("$", StringComparison.Ordinal) ? value.Substring(1) : value;
                separators.Add(item
                    .Replace("\\r\\n", "\r\n", StringComparison.Ordinal)
                    .Replace("\\n", "\n", StringComparison.Ordinal)
                    .Replace("\\r", "\r", StringComparison.Ordinal)
                    .Replace("\\t", "\t", StringComparison.Ordinal));
            }
            return separators;
        }

        private static (bool Nfc, bool Nfkc, bool LineEndings, bool Whitespace) ParseNormalization(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return (false, false, false, false);
            }

            bool nfc = false, nfkc = false, lineEndings = false, whitespace = false;
            foreach (string raw in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                switch (raw.ToLowerInvariant())
                {
                    case "nfc":
                        nfc = true;
                        break;
                    case "nfkc":
                        nfkc = true;
                        break;
                    case "line-endings":
                    case "lineendings":
                    case "crlf":
                        lineEndings = true;
                        break;
                    case "whitespace":
                        whitespace = true;
                        break;
                    case "all":
                        nfc = nfkc = lineEndings = whitespace = true;
                        break;
                    default:
                        throw new ArgumentException(
                            $"Неизвестная нормализация '{raw}'. Допустимые: nfc, nfkc, line-endings, whitespace, all.");
                }
            }
            return (nfc, nfkc, lineEndings, whitespace);
        }

        private static IReadOnlyDictionary<string, object?> ParseMetadata(string[]? values)
        {
            var metadata = new Dictionary<string, object?>();
            if (values is null)
            {
                return metadata;
            }

            foreach (string value in values)
            {
                int separator = value.IndexOf('=');
                if (separator <= 0)
                {
                    throw new ArgumentException($"Метаданные должны быть в формате key=value: '{value}'.");
                }
                metadata[value.Substring(0, separator)] = value.Substring(separator + 1);
            }
            return metadata;
        }
    }
}
