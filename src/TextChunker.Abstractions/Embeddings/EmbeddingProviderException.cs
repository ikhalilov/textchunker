namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// Embedding provider failure (server unavailable, timeout, invalid response,
/// wrong dimension). The CLI maps it to stderr + exit code 5, the API to HTTP 503.
/// </summary>
public sealed class EmbeddingProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>CLI exit code for this error.</summary>
    public int ExitCode => 5;
}
