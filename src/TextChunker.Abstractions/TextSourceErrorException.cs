namespace TextChunker.Abstractions;

/// <summary>Ошибка источника: файл не найден (exit 3), невалидная кодировка (exit 4), ошибка чтения (exit 4/6).</summary>
public sealed class TextSourceErrorException(int exitCode, string message) : Exception(message)
{
    /// <summary>Код завершения, соответствующий ошибке источника.</summary>
    public int ExitCode { get; } = exitCode;
}
