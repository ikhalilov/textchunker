namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// The document exceeds the semantic mode limit, and <c>--semantic-fallback error</c>
/// forbids a silent fallback. CLI: exit 2; API: HTTP 413.
/// </summary>
public sealed class SemanticLimitExceededException(string message) : Exception(message)
{
    /// <summary>CLI exit code.</summary>
    public int ExitCode => 2;

    /// <summary>HTTP status for the API.</summary>
    public int StatusCode => 413;
}
