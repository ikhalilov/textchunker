namespace TextChunker.Chunking.Semantic;

/// <summary>
/// Result of semantic boundary planning: atoms, pairwise distances between
/// neighbours and the indices of atoms at which a new chunk starts.
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

    /// <summary>Atoms of the input text.</summary>
    public IReadOnlyList<SemanticAtom> Atoms { get; }

    /// <summary><c>Distances[i]</c> is the distance (1 − cosine) between atoms <c>i</c> and <c>i+1</c>; length = atoms − 1.</summary>
    public IReadOnlyList<double> Distances { get; }

    /// <summary>Indices of atoms at which a new chunk starts (ascending, without zero).</summary>
    public IReadOnlyList<int> Breakpoints { get; }

    /// <summary>Boundary mask for O(1) checks in the chunker.</summary>
    public IReadOnlyList<bool> BreakpointMask { get; }

    public string Provider { get; }

    public string? Model { get; }

    public int Dimensions { get; }

    /// <summary>Embedding cache hits; null — the cache was not used.</summary>
    public long? EmbeddingCacheHits { get; }

    /// <summary>Embedding cache misses; null — the cache was not used.</summary>
    public long? EmbeddingCacheMisses { get; }

    /// <summary>Empty plan (text without atoms or without boundaries).</summary>
    public static SemanticPlan Empty(string provider = "none")
        => new([], [], [], provider, null, 0);

    public bool StartsNewChunk(int atomIndex)
        => atomIndex >= 0 && atomIndex < BreakpointMask.Count && BreakpointMask[atomIndex];
}
