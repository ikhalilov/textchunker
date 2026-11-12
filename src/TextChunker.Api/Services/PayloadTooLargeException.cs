namespace TextChunker.Api.Services;

/// <summary>Text longer than allowed (413).</summary>
public sealed class PayloadTooLargeException(string message) : Exception(message)
{
}
