namespace TextChunker.Chunking.Semantic;

/// <summary>
/// Результат планирования семантических границ: атомы, попарные расстояния между
/// соседями и индексы атомов, с которых начинается новый чанк.
/// </summary>
public sealed class SemanticPlan
{
    public SemanticPlan(
        IReadOnlyList<SemanticAtom> atoms,
        IReadOnlyList<double> distances,
        IReadOnlyList<int> breakpoints,
        string provider,
        string? model,
        int dimensions,
        long? embeddingCacheHits = null,
        long? embeddingCacheMisses = null)
    {
        Atoms = atoms ?? throw new ArgumentNullException(nameof(atoms));
        Distances = distances ?? throw new ArgumentNullException(nameof(distances));
        Breakpoints = breakpoints ?? throw new ArgumentNullException(nameof(breakpoints));
        Provider = provider;
        Model = model;
        Dimensions = dimensions;
        EmbeddingCacheHits = embeddingCacheHits;
        EmbeddingCacheMisses = embeddingCacheMisses;

        var mask = new bool[Math.Max(atoms.Count, 1)];
        foreach (int index in breakpoints)
        {
            if (index >= 0 && index < mask.Length)
            {
                mask[index] = true;
            }
        }
        BreakpointMask = mask;
    }

    /// <summary>Атомы входного текста.</summary>
    public IReadOnlyList<SemanticAtom> Atoms { get; }

    /// <summary><c>Distances[i]</c> — расстояние (1 − cosine) между атомами <c>i</c> и <c>i+1</c>; длина = atoms − 1.</summary>
    public IReadOnlyList<double> Distances { get; }

    /// <summary>Индексы атомов, с которых начинается новый чанк (возрастающие, без нуля).</summary>
    public IReadOnlyList<int> Breakpoints { get; }

    /// <summary>Маска границ для O(1)-проверки в чанкере.</summary>
    public IReadOnlyList<bool> BreakpointMask { get; }

    public string Provider { get; }

    public string? Model { get; }

    public int Dimensions { get; }

    /// <summary>Попадания в кэш эмбеддингов; null — кэш не использовался.</summary>
    public long? EmbeddingCacheHits { get; }

    /// <summary>Промахи кэша эмбеддингов; null — кэш не использовался.</summary>
    public long? EmbeddingCacheMisses { get; }

    /// <summary>Пустой план (текст без атомов или без границ).</summary>
    public static SemanticPlan Empty(string provider = "none")
        => new([], [], [], provider, null, 0);

    public bool StartsNewChunk(int atomIndex)
        => atomIndex >= 0 && atomIndex < BreakpointMask.Count && BreakpointMask[atomIndex];
}
