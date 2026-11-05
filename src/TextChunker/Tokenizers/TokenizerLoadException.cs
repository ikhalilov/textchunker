namespace TextChunker;

/// <summary>
/// Ошибка загрузки/инициализации модельного токенизатора (HF tokenizer.json,
/// модель, сетевая ошибка). Программа переводит в stderr + exit code 5 (task 3.5).
/// </summary>
public sealed class TokenizerLoadException : Exception
{
    public int ExitCode => 5;

    public TokenizerLoadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
