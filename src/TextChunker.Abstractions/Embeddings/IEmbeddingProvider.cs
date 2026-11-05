namespace TextChunker.Abstractions.Embeddings;

/// <summary>Необязательное расширение <see cref="IEmbeddingProvider"/>: имя модели в провайдере.</summary>
public interface IEmbeddingModelInfo
{
    /// <summary>Имя модели, передаваемое провайдеру (null — модель не именована).</summary>
    string? ModelName { get; }
}

/// <summary>
/// Провайдер эмбеддингов для семантической нарезки. Возвращает по одному вектору на
/// входной текст, в том же порядке. Векторы ожидаются L2-нормализованными
/// (провайдер нормализует сам, если сервер этого не делает).
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Имя провайдера (<c>llama-cpp</c>, <c>hash</c>).</summary>
    string Name { get; }

    /// <summary>Размерность векторов (0 — не проверяется).</summary>
    int Dimensions { get; }

    /// <summary>
    /// Эмбеддинги для <paramref name="texts"/>. Реализация обязана сохранять порядок
    /// и батнировать обращения к внешним сервисам сама.
    /// </summary>
    /// <exception cref="EmbeddingProviderException">Провайдер недоступен или вернул некорректные данные.</exception>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
