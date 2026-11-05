namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// Провайдер, который умеет сообщать статистику попаданий в кэш эмбеддингов.
/// Реализуется декоратором <c>CachedEmbeddingProvider</c>; планировщик пробрасывает
/// значения в <c>ChunkingStats</c> для диагностики стоимости прогона.
/// </summary>
public interface IEmbeddingCacheStats
{
    /// <summary>Сколько атомов обслужено из кэша.</summary>
    long CacheHits { get; }

    /// <summary>Сколько атомов пришлось вычислять заново.</summary>
    long CacheMisses { get; }
}
