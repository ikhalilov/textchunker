namespace TextChunker.Abstractions.Embeddings;

/// <summary>Optional extension of <see cref="IEmbeddingProvider"/>: the model name in the provider.</summary>
public interface IEmbeddingModelInfo
{
    /// <summary>Model name passed to the provider (null — the model is unnamed).</summary>
    string? ModelName { get; }
}

/// <summary>
/// Embedding provider for semantic chunking. Returns one vector per
/// input text, in the same order. Vectors are expected to be L2-normalized
/// (the provider normalizes them itself if the server does not).
/// </summary>
public interface IEmbeddingProvider
{
    /// <summary>Provider name (<c>llama-cpp</c>, <c>hash</c>).</summary>
    string Name { get; }

    /// <summary>Vector dimension (0 — not validated).</summary>
    int Dimensions { get; }

    /// <summary>
    /// Embeddings for <paramref name="texts"/>. The implementation must preserve the order
    /// and batch calls to external services itself.
    /// </summary>
    /// <exception cref="EmbeddingProviderException">The provider is unavailable or returned invalid data.</exception>
    Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default);
}
