namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// Ошибка провайдера эмбеддингов (сервер недоступен, таймаут, невалидный ответ,
/// неверная размерность). CLI переводит в stderr + exit code 5, API — в HTTP 503.
/// </summary>
public sealed class EmbeddingProviderException(string message, Exception? innerException = null)
    : Exception(message, innerException)
{
    /// <summary>Код завершения CLI для этой ошибки.</summary>
    public int ExitCode => 5;
}
