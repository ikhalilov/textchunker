namespace TextChunker.Api.Services;

/// <summary>Текст длиннее разрешённого (413).</summary>
public sealed class PayloadTooLargeException(string message) : Exception(message)
{
}
