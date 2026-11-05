namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// Документ больше лимита семантического режима, а <c>--semantic-fallback error</c>
/// запрещает тихий откат. CLI: exit 2; API: HTTP 413.
/// </summary>
public sealed class SemanticLimitExceededException(string message) : Exception(message)
{
    /// <summary>Код завершения CLI.</summary>
    public int ExitCode => 2;

    /// <summary>HTTP-статус для API.</summary>
    public int StatusCode => 413;
}
