namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// A provider that can report hit statistics for the embedding cache.
/// Implemented by the <c>CachedEmbeddingProvider</c> decorator; the scheduler passes the
/// values into <c>ChunkingStats</c> for diagnostics of the run's cost.
/// </summary>
public interface IEmbeddingCacheStats
{
    /// <summary>How many atoms were served from the cache.</summary>
    long CacheHits { get; }

    /// <summary>How many atoms had to be recomputed.</summary>
    long CacheMisses { get; }
}
