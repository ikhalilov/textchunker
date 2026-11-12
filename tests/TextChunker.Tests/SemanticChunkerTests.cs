using TextChunker.Abstractions.Embeddings;
using TextChunker.Chunking.Semantic;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

/// <summary>Atomization: sentences, Markdown, lists, code fences, abbreviations, offsets.</summary>
public sealed class SemanticAtomizerTests
{
    private static readonly WordTokenizer Tokenizer = new();

    private static readonly SemanticOptions GranularOptions = new() { MinAtomChars = 0, Window = 1 };

    private static IReadOnlyList<SemanticAtom> Atomize(string text, SemanticOptions? options = null)
        => SemanticAtomizer.Atomize(text, Tokenizer, options ?? GranularOptions);

    [Fact]
    public void EmptyTextProducesNoAtoms()
    {
        Assert.Empty(Atomize(string.Empty));
    }

    [Fact]
    public void SplitsSentencesOnTerminators()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("Первое предложение. Второе! Третье? Четвёртое…");

        Assert.Equal(
            new[] { "Первое предложение.", "Второе!", "Третье?", "Четвёртое…" },
            atoms.Select(a => a.Text).ToArray());
    }

    [Fact]
    public void KeepsQuotedTerminatorInsideSentence()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("Он сказал «привет.» Она ушла.");

        Assert.Equal(new[] { "Он сказал «привет.»", "Она ушла." }, atoms.Select(a => a.Text).ToArray());
    }

    [Theory]
    [InlineData("Это т. д. пример. Конец.", 2)]
    [InlineData("См. раздел 2. Он важнее.", 2)]
    [InlineData("И. И. Иванов пришёл. Все увидели.", 2)]
    [InlineData("Mr. Smith left. Nobody noticed.", 2)]
    [InlineData("См. рис. 1. Подробностей нет.", 2)]
    [InlineData("Цена 100 руб. 50 коп. Итог ясен.", 1)]
    public void DoesNotSplitOnAbbreviations(string text, int expectedAtoms)
    {
        Assert.Equal(expectedAtoms, Atomize(text).Count);
    }

    [Fact]
    public void DoesNotSplitDecimals()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("Стоимость 1.5 рубля. Это дёшево.");
        Assert.Equal(2, atoms.Count);
    }

    [Fact]
    public void BlankLineSeparatesParagraphs()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("Первый абзац.\nВторой абзац.");
        Assert.Equal(2, atoms.Count);

        IReadOnlyList<SemanticAtom> paragraphs = Atomize("Первый абзац.\n\nВторой абзац.");
        Assert.Equal(2, paragraphs.Count);
    }

    [Fact]
    public void SoftWrappedSentenceStaysOneAtom()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("Очень длинное предложение,\nразбитое на две строки.");
        Assert.Single(atoms);
        Assert.Equal("Очень длинное предложение,\nразбитое на две строки.", atoms[0].Text);
    }

    [Fact]
    public void MarkdownHeadingIsOwnAtom()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("# Заголовок\nТекст абзаца.");

        Assert.Equal(2, atoms.Count);
        Assert.Equal(SemanticAtomKind.Heading, atoms[0].Kind);
        Assert.Equal("# Заголовок", atoms[0].Text);
        Assert.Equal(SemanticAtomKind.Sentence, atoms[1].Kind);
    }

    [Fact]
    public void CodeFenceIsOneAtomAndIsNotSplit()
    {
        string text = "Введение.\n```csharp\nvar x = 1;\n// комментарий. Он не предложение.\n```\nЗаключение.";
        IReadOnlyList<SemanticAtom> atoms = Atomize(text);

        Assert.Equal(3, atoms.Count);
        Assert.Equal(SemanticAtomKind.Code, atoms[1].Kind);

        // Protection fences are preserved: the atom exactly covers the source region.
        Assert.Equal("```csharp\nvar x = 1;\n// комментарий. Он не предложение.\n```", atoms[1].Text);
        Assert.Equal("Введение.", atoms[0].Text);
        Assert.Equal("Заключение.", atoms[2].Text);
    }

    [Fact]
    public void UnclosedCodeFenceRunsToEndOfText()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("Введение.\n```\ncode line.\nanother line.");

        Assert.Equal(2, atoms.Count);
        Assert.Equal(SemanticAtomKind.Code, atoms[1].Kind);
        Assert.Equal("```\ncode line.\nanother line.", atoms[1].Text);
    }

    [Fact]
    public void ListItemsAreSeparateAtoms()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("- первый пункт.\n- второй пункт.\nОбычный текст.");

        Assert.Equal(3, atoms.Count);
        Assert.Equal(SemanticAtomKind.ListItem, atoms[0].Kind);
        Assert.Equal(SemanticAtomKind.ListItem, atoms[1].Kind);
        Assert.Equal("- первый пункт.", atoms[0].Text);
        Assert.Equal(SemanticAtomKind.Sentence, atoms[2].Kind);
    }

    [Fact]
    public void NumberedListItemsAreSeparateAtoms()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("1. первый.\n2) второй.");

        Assert.Equal(2, atoms.Count);
        Assert.All(atoms, atom => Assert.Equal(SemanticAtomKind.ListItem, atom.Kind));
    }

    [Fact]
    public void ListContinuationLineJoinsItem()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("- начало пункта,\n  продолжение того же пункта.");

        Assert.Single(atoms);
        Assert.Equal(SemanticAtomKind.ListItem, atoms[0].Kind);
        Assert.Contains("продолжение", atoms[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortAtomsAreMergedIntoNeighbour()
    {
        IReadOnlyList<SemanticAtom> merged = Atomize("Да. Нет. Возможно, это длинное предложение.");
        Assert.Equal(3, merged.Count);

        IReadOnlyList<SemanticAtom> withMin = SemanticAtomizer.Atomize(
            "Да. Нет. Возможно, это длинное предложение.",
            Tokenizer,
            new SemanticOptions { MinAtomChars = 20 });

        Assert.Equal(2, withMin.Count);
        Assert.Contains("Да.", withMin[0].Text, StringComparison.Ordinal);
        Assert.Contains("Нет.", withMin[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void ShortLineBeforeParagraphStartsMergedAtom()
    {
        const string text =
            "Длинное предложение про договор и споры сторон.\n\n" +
            "Охрана труда\n\n" +
            "Работодатель обеспечивает инструктаж перед допуском к работе.";

        IReadOnlyList<SemanticAtom> atoms = SemanticAtomizer.Atomize(
            text, Tokenizer, new SemanticOptions { MinAtomChars = 20 });

        Assert.Equal(2, atoms.Count);
        Assert.StartsWith("Охрана труда", atoms[1].Text, StringComparison.Ordinal);
    }

    [Fact]
    public void OversizedAtomIsSplitByTokenLimit()
    {
        string text = string.Join(" ", Enumerable.Range(0, 30).Select(i => $"слово{i}"));
        IReadOnlyList<SemanticAtom> atoms = Atomize(text + ".", GranularOptions with { MaxAtomTokens = 7 });

        Assert.True(atoms.Count > 1, "длинный атом должен дробиться");
        Assert.All(atoms, atom => Assert.True(Tokenizer.CountTokens(atom.Text) <= 7));

        string reconstructed = string.Concat(atoms.Select(a => a.Text));
        Assert.Equal(text.Replace(" ", string.Empty), reconstructed.Replace(" ", string.Empty).Replace(".", string.Empty));
    }

    [Fact]
    public void OffsetsPointAtOriginalText()
    {
        const string text = "# Заголовок\nПервое предложение.\n\n- пункт один\n- пункт два\n```code```";
        IReadOnlyList<SemanticAtom> atoms = Atomize(text);

        foreach (SemanticAtom atom in atoms)
        {
            Assert.InRange(atom.Start, 0, text.Length);
            Assert.InRange(atom.End, atom.Start, text.Length);
            Assert.Equal(text.Substring(atom.Start, atom.End - atom.Start), atom.Text);
        }

        for (int i = 1; i < atoms.Count; i++)
        {
            Assert.True(atoms[i].Start >= atoms[i - 1].End, "атомы не должны перекрываться");
        }
    }

    [Fact]
    public void CrLfIsHandledWithoutStrayCarriageReturns()
    {
        IReadOnlyList<SemanticAtom> atoms = Atomize("Первое.\r\nВторое.\r\n");

        Assert.Equal(2, atoms.Count);
        Assert.All(atoms, atom => Assert.DoesNotContain('\r', atom.Text));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => SemanticAtomizer.Atomize(null!, Tokenizer, GranularOptions));
        Assert.Throws<ArgumentNullException>(() => SemanticAtomizer.Atomize("x", null!, GranularOptions));
        Assert.Throws<ArgumentNullException>(() => SemanticAtomizer.Atomize("x", Tokenizer, null!));
    }
}

/// <summary>Semantic boundary planner on scripted vectors (without a real model).</summary>
public sealed class SemanticPlannerTests
{
    private static readonly WordTokenizer Tokenizer = new();

    private static readonly float[] AxisA = { 1f, 0f };
    private static readonly float[] AxisB = { 0f, 1f };

    /// <summary>A provider that returns vectors from a dictionary keyed by the atom text.</summary>
    private sealed class ScriptedProvider : IEmbeddingProvider, IEmbeddingModelInfo
    {
        private readonly Dictionary<string, float[]> _vectors;

        public ScriptedProvider(Dictionary<string, float[]> vectors) => _vectors = vectors;

        public string Name => "scripted";

        public int Dimensions => 2;

        public string? ModelName => "scripted-model";

        public List<int> Calls { get; } = new();

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            Calls.Add(texts.Count);
            var result = texts.Select(text => _vectors[text]).ToList();
            return Task.FromResult<IReadOnlyList<float[]>>(result);
        }
    }

    private static SemanticOptions PlanOptions => new()
    {
        MinAtomChars = 0,
        Window = 1,
        Percentile = 0.9,
        MinDistance = 0.02
    };

    /// <summary>Six sentences: the first three along axis A, the last three along axis B.</summary>
    private static (string Text, Dictionary<string, float[]> Vectors) ClusteredDocument()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["One."] = AxisA,
            ["Two."] = AxisA,
            ["Three."] = AxisA,
            ["Four."] = AxisB,
            ["Five."] = AxisB,
            ["Six."] = AxisB
        };
        return ("One. Two. Three. Four. Five. Six.", vectors);
    }

    [Fact]
    public async Task PlacesBreakpointAtTopicChange()
    {
        (string text, Dictionary<string, float[]> vectors) = ClusteredDocument();
        var planner = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions);

        SemanticPlan plan = await planner.PlanAsync(text, Tokenizer);

        Assert.Equal(6, plan.Atoms.Count);
        Assert.Equal(new[] { 3 }, plan.Breakpoints);
        Assert.Equal("scripted", plan.Provider);
        Assert.Equal("scripted-model", plan.Model);
        Assert.Equal(2, plan.Dimensions);
    }

    [Fact]
    public async Task HeadingsAlwaysStartNewChunk()
    {
        const string text = "One. Two.\n\n# Section\n\nThree. Four.";
        var vectors = new Dictionary<string, float[]>
        {
            ["One."] = AxisA,
            ["Two."] = AxisA,
            ["# Section"] = AxisA,
            ["Three."] = AxisA,
            ["Four."] = AxisA
        };

        var planner = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions);
        SemanticPlan plan = await planner.PlanAsync(text, Tokenizer);

        Assert.Equal(5, plan.Atoms.Count);
        Assert.Equal(new[] { 2 }, plan.Breakpoints);
    }

    [Fact]
    public async Task WindowAveragesNeighboursLikeTextTiling()
    {
        (string text, Dictionary<string, float[]> vectors) = ClusteredDocument();
        var planner = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions with { Window = 3 });

        SemanticPlan plan = await planner.PlanAsync(text, Tokenizer);

        Assert.Equal(new[] { 3 }, plan.Breakpoints);

        // Window averages: cos(A, (2A+B)/3) = 2/√5 → d = 1 − 2/√5, etc.
        double[] expected =
        {
            1.0 - 2.0 / Math.Sqrt(5.0),
            1.0 - 1.0 / Math.Sqrt(5.0),
            1.0,
            1.0 - 1.0 / Math.Sqrt(5.0),
            1.0 - 2.0 / Math.Sqrt(5.0)
        };
        Assert.Equal(expected.Length, plan.Distances.Count);
        for (int i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i], plan.Distances[i], 4);
        }
    }

    [Fact]
    public async Task NoBreakpointsWhenAllDistancesIdentical()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["One."] = AxisA,
            ["Two."] = AxisA,
            ["Three."] = AxisA
        };
        var planner = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions);

        SemanticPlan plan = await planner.PlanAsync("One. Two. Three.", Tokenizer);

        Assert.Equal(3, plan.Atoms.Count);
        Assert.Empty(plan.Breakpoints);
    }

    [Fact]
    public async Task MinDistanceSuppressesWeakBoundaries()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["One."] = AxisA,
            ["Two."] = new[] { (float)Math.Cos(0.05), (float)Math.Sin(0.05) },
            ["Three."] = AxisA
        };
        var planner = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions with { MinDistance = 0.05 });

        SemanticPlan plan = await planner.PlanAsync("One. Two. Three.", Tokenizer);

        Assert.Empty(plan.Breakpoints);
    }

    [Fact]
    public async Task SingleAtomProducesNoDistances()
    {
        var vectors = new Dictionary<string, float[]> { ["Only."] = AxisA };
        var planner = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions);

        SemanticPlan plan = await planner.PlanAsync("Only.", Tokenizer);

        Assert.Single(plan.Atoms);
        Assert.Empty(plan.Distances);
        Assert.Empty(plan.Breakpoints);
    }

    [Fact]
    public async Task EmptyTextProducesEmptyPlan()
    {
        var planner = new SemanticPlanner(new ScriptedProvider(new Dictionary<string, float[]>()), PlanOptions);

        SemanticPlan plan = await planner.PlanAsync(string.Empty, Tokenizer);

        Assert.Empty(plan.Atoms);
        Assert.Equal("scripted", plan.Provider);
    }

    [Fact]
    public async Task LowerPercentileProducesMoreBreakpoints()
    {
        (string text, Dictionary<string, float[]> vectors) = ClusteredDocument();
        var strict = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions with { Percentile = 0.9, MinDistance = 0 });
        var loose = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions with { Percentile = 0.5, MinDistance = 0 });

        SemanticPlan strictPlan = await strict.PlanAsync(text, Tokenizer);
        SemanticPlan loosePlan = await loose.PlanAsync(text, Tokenizer);

        Assert.Single(strictPlan.Breakpoints);
        Assert.True(loosePlan.Breakpoints.Count > strictPlan.Breakpoints.Count);
        Assert.All(loosePlan.Breakpoints, index => Assert.True(index > 0 && index < loosePlan.Atoms.Count));
    }

    [Fact]
    public async Task PlanIsDeterministic()
    {
        (string text, Dictionary<string, float[]> vectors) = ClusteredDocument();

        SemanticPlan first = await new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions).PlanAsync(text, Tokenizer);
        SemanticPlan second = await new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions).PlanAsync(text, Tokenizer);

        Assert.Equal(first.Breakpoints, second.Breakpoints);
        Assert.Equal(first.Distances, second.Distances);
        Assert.Equal(first.Atoms.Select(a => (a.Start, a.End)), second.Atoms.Select(a => (a.Start, a.End)));
    }

    [Fact]
    public async Task BreakpointMaskMatchesBreakpoints()
    {
        (string text, Dictionary<string, float[]> vectors) = ClusteredDocument();
        SemanticPlan plan = await new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions).PlanAsync(text, Tokenizer);

        Assert.False(plan.StartsNewChunk(0));
        Assert.True(plan.StartsNewChunk(3));
        Assert.False(plan.StartsNewChunk(plan.Atoms.Count));
        Assert.False(plan.StartsNewChunk(-1));
    }

    [Fact]
    public async Task ProviderCountMismatchThrowsEmbeddingException()
    {
        var planner = new SemanticPlanner(new ShortVectorProvider(), PlanOptions);

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => planner.PlanAsync("One. Two. Three.", Tokenizer));

        Assert.Equal(5, exception.ExitCode);
    }

    [Fact]
    public async Task RaggedDimensionsThrowEmbeddingException()
    {
        var vectors = new Dictionary<string, float[]>
        {
            ["One."] = new float[] { 1, 0 },
            ["Two."] = new float[] { 1, 0, 0 }
        };
        var planner = new SemanticPlanner(new ScriptedProvider(vectors), PlanOptions);

        await Assert.ThrowsAsync<EmbeddingProviderException>(() => planner.PlanAsync("One. Two.", Tokenizer));
    }

    [Fact]
    public async Task CancellationPropagates()
    {
        (string text, Dictionary<string, float[]> vectors) = ClusteredDocument();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var planner = new SemanticPlanner(new ThrowingProvider(cancellation.Token), PlanOptions);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => planner.PlanAsync(text, Tokenizer, cancellation.Token));
    }

    [Fact]
    public void RejectsNullDependencies()
    {
        Assert.Throws<ArgumentNullException>(() => new SemanticPlanner(null!, PlanOptions));
        Assert.Throws<ArgumentNullException>(() => new SemanticPlanner(new ScriptedProvider(new()), null!));
    }

    [Theory]
    [InlineData(new[] { 0.0, 0.1, 0.5 }, 0.9, 0.5)]
    [InlineData(new[] { 0.0, 0.1, 0.5 }, 0.5, 0.1)]
    [InlineData(new[] { 0.0, 0.1, 0.5 }, 0.1, 0.0)]
    [InlineData(new[] { 0.0, 0.1, 0.5 }, 1.0, 0.5)]
    [InlineData(new[] { 0.4 }, 0.5, 0.4)]
    public void PercentileUsesNearestRank(double[] values, double percentile, double expected)
    {
        Assert.Equal(expected, SemanticPlanner.Percentile(values, percentile), 10);
    }

    private sealed class ShortVectorProvider : IEmbeddingProvider
    {
        public string Name => "short";

        public int Dimensions => 2;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<float[]>>(new List<float[]> { AxisA });
    }

    private sealed class ThrowingProvider : IEmbeddingProvider
    {
        private readonly CancellationToken _token;

        public ThrowingProvider(CancellationToken token) => _token = token;

        public string Name => "throwing";

        public int Dimensions => 2;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _token.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<float[]>>(texts.Select(_ => AxisA).ToList());
        }
    }
}
