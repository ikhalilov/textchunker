namespace TextChunker.Abstractions;

/// <summary>Source error: file not found (exit 3), invalid encoding (exit 4), read error (exit 4/6).</summary>
public sealed class TextSourceErrorException(int exitCode, string message) : Exception(message)
{
    /// <summary>Exit code corresponding to the source error.</summary>
    public int ExitCode { get; } = exitCode;
}
