using TextChunker.Abstractions.Embeddings;

namespace TextChunker.Embeddings;

/// <summary>
/// Декоратор провайдера эмбеддингов с дисковым кэшем: сначала читает кэш, недостающие
/// тексты отправляет внутрь пакетами и дописывает результат в кэш. Порядок и размерность
/// совпадают с внутренним провайдером; статистика попаданий доступна для <c>--stats</c>.
/// </summary>
public sealed class CachedEmbeddingProvider : IEmbeddingProvider, IEmbeddingModelInfo, IEmbeddingCacheStats
{
    private readonly IEmbeddingProvider _inner;
    private readonly EmbeddingCache _cache;

    public CachedEmbeddingProvider(IEmbeddingProvider inner, EmbeddingCache cache)
    {
        _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
    }

    public string Name => _inner.Name;

    public int Dimensions => _inner.Dimensions;

    /// <summary>Внутренний провайдер (для диагностики/тестов).</summary>
    public IEmbeddingProvider Inner => _inner;

    /// <summary>Каталог области кэша (для диагностики/тестов).</summary>
    public string? ScopeDirectory => _cache.ScopeDirectory;

    string? IEmbeddingModelInfo.ModelName => _inner is IEmbeddingModelInfo info ? info.ModelName : null;

    public long CacheHits { get; private set; }

    public long CacheMisses { get; private set; }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts is null)
        {
            throw new ArgumentNullException(nameof(texts));
        }
        if (texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        var result = new float[texts.Count][];
        var missing = new List<int>();

        for (int i = 0; i < texts.Count; i++)
        {
            if (_cache.TryGet(texts[i], out float[] cached)
                && (_inner.Dimensions == 0 || cached.Length == _inner.Dimensions))
            {
                result[i] = cached;
                CacheHits++;
            }
            else
            {
                missing.Add(i);
            }
        }

        if (missing.Count > 0)
        {
            CacheMisses += missing.Count;

            var toEmbed = new List<string>(missing.Count);
            foreach (int index in missing)
            {
                toEmbed.Add(texts[index]);
            }

            IReadOnlyList<float[]> vectors = await _inner.EmbedAsync(toEmbed, cancellationToken).ConfigureAwait(false);
            if (vectors.Count != missing.Count)
            {
                throw new EmbeddingProviderException(
                    $"Провайдер {_inner.Name} вернул {vectors.Count} эмбеддингов вместо {missing.Count}.");
            }

            for (int j = 0; j < missing.Count; j++)
            {
                int index = missing[j];
                result[index] = vectors[j];
                _cache.Set(texts[index], vectors[j]);
            }
        }

        return result;
    }
}
