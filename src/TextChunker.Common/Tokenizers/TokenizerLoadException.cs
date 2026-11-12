namespace TextChunker;

/// <summary>
/// Error loading/initializing a model tokenizer (HF tokenizer.json,
/// model, network error). The program maps it to stderr + exit code 5 (task 3.5).
/// </summary>
public sealed class TokenizerLoadException : Exception
{
    public int ExitCode => 5;

    public TokenizerLoadException(string message, Exception? innerException = null)
        : base(message, innerException)
    {
    }
}
