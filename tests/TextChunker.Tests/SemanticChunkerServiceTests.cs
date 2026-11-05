using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;
using TextChunker.Chunking;
using TextChunker.Chunking.Semantic;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

/// <summary>Группировка атомов в чанки по семантическим границам.</summary>
public sealed class SemanticChunkerTests
{
    private static readonly WordTokenizer Tokenizer = new();

    /// <summary>План: 6 предложений, граница перед атомом 3.</summary>
    private static SemanticPlan Plan(string text, params int[] breakpoints)
    {
        IReadOnlyList<SemanticAtom> atoms = SemanticAtomizer.Atomize(
            text, Tokenizer, new SemanticOptions { MinAtomChars = 0 });

        var distances = new double[Math.Max(atoms.Count - 1, 0)];
        for (int i = 0; i < distances.Length; i++)
        {
            distances[i] = breakpoints.Contains(i + 1) ? 0.9 : 0.1;
        }

        return new SemanticPlan(atoms, distances, breakpoints, "hash", null, 8);
    }

    private static ChunkSplit Run(string text, SemanticPlan plan, ChunkingOptions options, bool final = true)
    {
        var chunker = new SemanticChunker();
        chunker.AttachPlan(plan);
        return chunker.Chunk(text, Tokenizer, options, final);
    }

    [Fact]
    public void GroupsAtomsUntilSemanticBoundary()
    {
        const string text = "One. Two. Three. Four. Five. Six.";
        SemanticPlan plan = Plan(text, new[] { 3 }.ToArray());

        ChunkSplit split = Run(text, plan, new ChunkingOptions(maxTokens: 100, trim: false));

        Assert.Equal(2, split.Segments.Count);
        Assert.Equal("One. Two. Three.", split.Segments[0].Text);
        Assert.Equal("Four. Five. Six.", split.Segments[1].Text);
    }

    [Fact]
    public void WithoutBoundariesItIsOneChunkWhenItFits()
    {
        const string text = "One. Two. Three.";
        ChunkSplit split = Run(text, Plan(text), new ChunkingOptions(maxTokens: 100, trim: false));

        Assert.Single(split.Segments);
    }

    [Fact]
    public void SplitsWhenTokenLimitExceeded()
    {
        const string text = "one two three four five six seven eight";
        ChunkSplit split = Run(text, Plan(text), new ChunkingOptions(maxTokens: 4, trim: false));

        Assert.True(split.Segments.Count >= 2);
        foreach (ChunkSegment segment in split.Segments)
        {
            Assert.True(Tokenizer.CountTokens(segment.Text) <= 4, $"'{segment.Text}' > 4 токенов");
        }
    }

    [Fact]
    public void OversizedAtomIsSplitOnWordBoundary()
    {
        string text = string.Join(" ", Enumerable.Range(0, 40).Select(i => $"word{i}"));
        ChunkSplit split = Run(text, Plan(text), new ChunkingOptions(maxTokens: 5, trim: false));

        Assert.True(split.Segments.Count > 1);
        foreach (ChunkSegment segment in split.Segments)
        {
            Assert.True(Tokenizer.CountTokens(segment.Text) <= 5);
        }

        // Рез выровнен на пробел: обрывков слов нет (все куски начинаются с «word»).
        Assert.All(split.Segments, segment => Assert.StartsWith("word", segment.Text, StringComparison.Ordinal));
        Assert.Equal(text.Replace(" ", string.Empty), string.Concat(split.Segments.Select(s => s.Text)).Replace(" ", string.Empty));
    }

    [Fact]
    public void PrefixCountsTowardTokenLimit()
    {
        const string text = "One. Two. Three. Four. Five. Six.";
        var options = new ChunkingOptions(maxTokens: 6, prefix: "none ", trim: false);

        ChunkSplit split = Run(text, Plan(text, new[] { 3 }.ToArray()), options);

        Assert.NotEmpty(split.Segments);
        foreach (ChunkSegment segment in split.Segments)
        {
            Assert.True(Tokenizer.CountTokens("none " + segment.Text) <= 6);
        }
    }

    [Fact]
    public void OverlapRepeatsTrailingAtoms()
    {
        const string text = "One. Two. Three. Four. Five. Six.";
        var options = new ChunkingOptions(maxTokens: 100, overlapTokens: 2, trim: false);

        ChunkSplit split = Run(text, Plan(text, new[] { 3 }.ToArray()), options);

        Assert.Equal(2, split.Segments.Count);
        // Хвост первого чанка («Two. Three.») повторяется в начале второго.
        Assert.Equal("One. Two. Three.", split.Segments[0].Text);
        Assert.StartsWith("Two.", split.Segments[1].Text, StringComparison.Ordinal);
        Assert.Contains("Six.", split.Segments[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void EmptyPlanTextProducesNothing()
    {
        ChunkSplit split = Run(string.Empty, SemanticPlan.Empty("hash"), new ChunkingOptions(maxTokens: 10));
        Assert.Empty(split.Segments);
        Assert.Equal(0, split.ConsumedEnd);
    }

    [Fact]
    public void NonFinalCallRequestsMoreText()
    {
        const string text = "One. Two.";
        SemanticPlan plan = Plan(text);

        ChunkSplit split = Run(text, plan, new ChunkingOptions(maxTokens: 100), final: false);

        Assert.Empty(split.Segments);
        Assert.True(split.MoreNeeded);
        Assert.Equal(0, split.ConsumedEnd);
    }

    [Fact]
    public void ChunkWithoutPlanThrows()
    {
        var chunker = new SemanticChunker();
        Assert.Throws<InvalidOperationException>(
            () => chunker.Chunk("One.", Tokenizer, new ChunkingOptions(maxTokens: 10)));
    }

    [Fact]
    public void OffsetsMatchEmittedText()
    {
        const string text = "One. Two. Three. Four. Five. Six.";
        ChunkSplit split = Run(text, Plan(text, new[] { 3 }.ToArray()), new ChunkingOptions(maxTokens: 100, trim: false));

        foreach (ChunkSegment segment in split.Segments)
        {
            Assert.Equal(text.Substring(segment.StartInText, segment.EndInText - segment.StartInText), segment.Text);
        }
        Assert.Equal(text.Length, split.ConsumedEnd);
    }
}

/// <summary>Сервис: document-mode, лимит объёма, метаданные, откат на recursive.</summary>
public sealed class SemanticServiceTests
{
    private static readonly WordTokenizer Tokenizer = new();

    private sealed class StubPlanner : ISemanticPlanner
    {
        private readonly int[] _breakpoints;
        private readonly long? _cacheHits;
        private readonly long? _cacheMisses;

        public StubPlanner(params int[] breakpoints)
            : this(breakpoints, null, null)
        {
        }

        public StubPlanner(int[] breakpoints, long? cacheHits, long? cacheMisses)
        {
            _breakpoints = breakpoints;
            _cacheHits = cacheHits;
            _cacheMisses = cacheMisses;
        }

        public string? LastText { get; private set; }

        public Task<SemanticPlan> PlanAsync(string text, ITokenizer tokenizer, CancellationToken cancellationToken = default)
        {
            LastText = text;
            IReadOnlyList<SemanticAtom> atoms = SemanticAtomizer.Atomize(
                text, tokenizer, new SemanticOptions { MinAtomChars = 0 });
            var distances = new double[Math.Max(atoms.Count - 1, 0)];
            return Task.FromResult(new SemanticPlan(
                atoms, distances, _breakpoints, "hash", "stub-model", 8, _cacheHits, _cacheMisses));
        }
    }

    private static async Task<CollectingWriter> RunAsync(
        string[] lines,
        ChunkingOptions options,
        ISemanticPlanner? planner = null,
        SemanticOptions? semantic = null)
    {
        var writer = new CollectingWriter();
        var service = new ChunkingService(
            Tokenizer,
            new SemanticChunker(),
            options,
            semanticPlanner: planner ?? new StubPlanner(3),
            semanticOptions: semantic);
        await service.ProcessAsync(new FakeSource(lines), writer);
        return writer;
    }

    [Fact]
    public async Task ProducesChunksWithSemanticMetadata()
    {
        CollectingWriter writer = await RunAsync(
            new[] { "One. Two. Three. Four. Five. Six." },
            new ChunkingOptions(strategy: "semantic", maxTokens: 100, trim: false));

        Assert.Equal(2, writer.Chunks.Count);
        Assert.Equal("One. Two. Three.", writer.Chunks[0].Text);

        var metadata = writer.Chunks[0].Metadata;
        Assert.Equal("semantic", metadata["strategy"]);
        Assert.Equal("hash", metadata["semanticProvider"]);
        Assert.Equal("stub-model", metadata["semanticModel"]);
        Assert.Equal(0.9, metadata["breakpointPercentile"]);
        Assert.False(metadata.ContainsKey("semanticFallback"));
    }

    [Fact]
    public async Task StatsExposeSemanticAtomCountAndCacheCounters()
    {
        var writer = new CollectingWriter();
        var service = new ChunkingService(
            Tokenizer,
            new SemanticChunker(),
            new ChunkingOptions(strategy: "semantic", maxTokens: 100, trim: false),
            semanticPlanner: new StubPlanner(new[] { 3 }, cacheHits: 5, cacheMisses: 2));

        ChunkingStats stats = await service.ProcessAsync(
            new FakeSource(new[] { "One. Two. Three. Four. Five. Six." }),
            writer);

        Assert.Equal(6, stats.SemanticAtomCount);
        Assert.Equal(5, stats.EmbeddingCacheHits);
        Assert.Equal(2, stats.EmbeddingCacheMisses);
    }

    [Fact]
    public async Task StatsIncludeSemanticPlanningTime()
    {
        var writer = new CollectingWriter();
        var service = new ChunkingService(
            Tokenizer,
            new SemanticChunker(),
            new ChunkingOptions(strategy: "semantic", maxTokens: 100),
            semanticPlanner: new SlowPlanner(TimeSpan.FromMilliseconds(80)));

        ChunkingStats stats = await service.ProcessAsync(
            new FakeSource(new[] { "One. Two." }),
            writer);

        Assert.True(stats.ElapsedMs >= 50, $"elapsedMs={stats.ElapsedMs} не включает планирование");
    }

    private sealed class SlowPlanner : ISemanticPlanner
    {
        private readonly TimeSpan _delay;

        public SlowPlanner(TimeSpan delay) => _delay = delay;

        public async Task<SemanticPlan> PlanAsync(
            string text,
            ITokenizer tokenizer,
            CancellationToken cancellationToken = default)
        {
            await Task.Delay(_delay, cancellationToken);
            IReadOnlyList<SemanticAtom> atoms = SemanticAtomizer.Atomize(
                text, tokenizer, new SemanticOptions { MinAtomChars = 0 });
            return new SemanticPlan(
                atoms,
                new double[Math.Max(atoms.Count - 1, 0)],
                Array.Empty<int>(),
                "hash",
                "stub-model",
                8);
        }
    }

    private static async Task<(CollectingWriter Writer, ChunkingStats Stats)> RunStreamingAsync(
        string[] lines,
        SemanticOptions semantic,
        ChunkingOptions options,
        ISemanticPlanner? planner = null)
    {
        var writer = new CollectingWriter();
        var service = new ChunkingService(
            Tokenizer,
            new SemanticChunker(),
            options,
            semanticPlanner: planner ?? new StubPlanner(),
            semanticOptions: semantic);
        ChunkingStats stats = await service.ProcessAsync(new FakeSource(lines), writer);
        return (writer, stats);
    }

    [Fact]
    public async Task StreamingModeCoversDocumentWithoutGaps()
    {
        string[] lines = Enumerable.Range(0, 30)
            .Select(i => $"Строка {i}: это достаточно длинное предложение для проверки окон.")
            .ToArray();
        var semantic = new SemanticOptions
        {
            Fallback = SemanticFallbackMode.Streaming,
            StreamWindowBytes = 300,
            StreamOverlapBytes = 80
        };
        var options = new ChunkingOptions(
            strategy: "semantic",
            maxTokens: 40,
            minTokens: 39,
            dropSmall: true);

        (CollectingWriter writer, ChunkingStats stats) = await RunStreamingAsync(lines, semantic, options);

        Assert.NotEmpty(writer.Chunks);
        Assert.Equal(writer.Chunks.Count, stats.ChunkCount);
        Assert.Equal(1, writer.Chunks[0].Seq);

        string source = string.Join("\n", lines);
        for (int i = 0; i < writer.Chunks.Count; i++)
        {
            Chunk chunk = writer.Chunks[i];
            Assert.Equal(i + 1, chunk.Seq);
            Assert.Equal($"test:{i + 1:D6}", chunk.Id);
            Assert.Equal("streaming", chunk.Metadata["semanticFallback"]);
            Assert.NotNull(chunk.StartChar);
            Assert.NotNull(chunk.EndChar);
            Assert.InRange(chunk.StartChar!.Value, 0, source.Length);
            Assert.InRange(chunk.EndChar!.Value, chunk.StartChar.Value, source.Length);
        }

        for (int i = 1; i < writer.Chunks.Count; i++)
        {
            Chunk previous = writer.Chunks[i - 1];
            Chunk current = writer.Chunks[i];
            Assert.True(current.StartChar >= previous.StartChar, "смещения должны быть монотонны");
            if (current.StartChar > previous.EndChar)
            {
                string gap = source.Substring(
                    (int)previous.EndChar!.Value,
                    (int)(current.StartChar!.Value - previous.EndChar.Value));
                Assert.True(gap.All(char.IsWhiteSpace), $"разрыв в тексте: '{gap}'");
            }
        }
    }

    [Fact]
    public async Task StreamingModeIgnoresMaxBytes()
    {
        string[] lines =
        {
            "Первое длинное предложение про установку сервера.",
            "Второе длинное предложение про фондовый рынок.",
            "Третье длинное предложение про рецепт пасты."
        };
        var semantic = new SemanticOptions
        {
            Fallback = SemanticFallbackMode.Streaming,
            MaxBytes = 10,
            StreamWindowBytes = 200,
            StreamOverlapBytes = 40
        };

        (CollectingWriter writer, _) = await RunStreamingAsync(
            lines,
            semantic,
            new ChunkingOptions(strategy: "semantic", maxTokens: 40));

        Assert.NotEmpty(writer.Chunks);
        Assert.All(writer.Chunks, chunk => Assert.Equal("streaming", chunk.Metadata["semanticFallback"]));
        Assert.DoesNotContain(writer.Chunks, chunk => Equals(chunk.Metadata["semanticFallback"], "recursive"));
    }

    [Fact]
    public void StreamingOverlapMustBeLessThanWindow()
    {
        var semantic = new SemanticOptions
        {
            StreamWindowBytes = 100,
            StreamOverlapBytes = 100
        };

        Assert.Throws<ArgumentOutOfRangeException>(() => semantic.Validate());
    }

    [Fact]
    public async Task NonSemanticStatsHaveNoSemanticDiagnostics()
    {
        var writer = new CollectingWriter();
        var service = new ChunkingService(
            Tokenizer,
            new RecursiveChunker(),
            new ChunkingOptions(strategy: "recursive", maxTokens: 100));

        ChunkingStats stats = await service.ProcessAsync(new FakeSource(new[] { "One. Two." }), writer);

        Assert.Null(stats.SemanticAtomCount);
        Assert.Null(stats.EmbeddingCacheHits);
        Assert.Null(stats.EmbeddingCacheMisses);
    }

    [Fact]
    public async Task MultilineInputKeepsOffsetsInBothLines()
    {
        CollectingWriter writer = await RunAsync(
            new[] { "One. Two.", "Three. Four." },
            new ChunkingOptions(strategy: "semantic", maxTokens: 100, trim: false),
            new StubPlanner());

        Assert.Single(writer.Chunks);
        Assert.Equal("One. Two.\nThree. Four.", writer.Chunks[0].Text);
        Assert.Equal(0L, writer.Chunks[0].StartChar);
        Assert.Equal(22L, writer.Chunks[0].EndChar);
    }

    [Fact]
    public async Task PlannerReceivesWholeDocument()
    {
        var planner = new StubPlanner();
        await RunAsync(
            new[] { "One.", "Two." },
            new ChunkingOptions(strategy: "semantic", maxTokens: 100),
            planner);

        Assert.Equal("One.\nTwo.", planner.LastText);
    }

    [Fact]
    public async Task OversizedDocumentFallsBackToRecursive()
    {
        var semantic = new SemanticOptions { MaxBytes = 10, Fallback = SemanticFallbackMode.Recursive };
        CollectingWriter writer = await RunAsync(
            new[] { new string('a', 50) },
            new ChunkingOptions(strategy: "semantic", maxChars: 10, trim: false),
            new StubPlanner(),
            semantic);

        Assert.NotEmpty(writer.Chunks);
        Assert.Equal("recursive", writer.Chunks[0].Metadata["semanticFallback"]);
    }

    [Fact]
    public async Task OversizedDocumentWithErrorFallbackThrows()
    {
        var semantic = new SemanticOptions { MaxBytes = 10, Fallback = SemanticFallbackMode.Error };

        var exception = await Assert.ThrowsAsync<SemanticLimitExceededException>(() => RunAsync(
            new[] { new string('a', 50) },
            new ChunkingOptions(strategy: "semantic", maxChars: 10),
            new StubPlanner(),
            semantic));

        Assert.Equal(2, exception.ExitCode);
        Assert.Equal(413, exception.StatusCode);
    }

    [Fact]
    public async Task NonSemanticStrategyIgnoresPlanner()
    {
        var planner = new StubPlanner();
        var writer = new CollectingWriter();
        var service = new ChunkingService(
            Tokenizer,
            new RecursiveChunker(),
            new ChunkingOptions(strategy: "recursive", maxChars: 10),
            semanticPlanner: planner);

        await service.ProcessAsync(new FakeSource(new[] { "abcdefghij" }), writer);

        Assert.Null(planner.LastText);
        Assert.Single(writer.Chunks);
    }

    [Fact]
    public async Task SemanticWithoutPlannerThrows()
    {
        var service = new ChunkingService(
            Tokenizer,
            new SemanticChunker(),
            new ChunkingOptions(strategy: "semantic", maxTokens: 10));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            service.ProcessAsync(new FakeSource(new[] { "One." }), new CollectingWriter()));
    }

    [Fact]
    public void FactoryCreatesSemanticStrategy()
    {
        Assert.IsType<SemanticChunker>(ChunkerFactory.Create("semantic"));
        Assert.Contains("semantic", ChunkerFactory.SupportedStrategies);
        Assert.True(ChunkerFactory.RequiresEmbeddings("semantic"));
        Assert.False(ChunkerFactory.RequiresEmbeddings("recursive"));
    }

    [Fact]
    public void PrefixEqualToLimitIsRejected()
    {
        var options = new ChunkingOptions(strategy: "semantic", maxTokens: 4, prefix: "title: none | text: ");

        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkingService(
            Tokenizer,
            new SemanticChunker(),
            options,
            semanticPlanner: new StubPlanner()));
    }

    [Fact]
    public void SemanticStrategyRejectsInvalidSemanticOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ChunkingService(
            Tokenizer,
            new SemanticChunker(),
            new ChunkingOptions(strategy: "semantic", maxTokens: 10),
            semanticPlanner: new StubPlanner(),
            semanticOptions: new SemanticOptions { Percentile = 1.5 }));
    }
}
