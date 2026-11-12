namespace TextChunker.Api.Services;

/// <summary>Unsupported uploaded file type (415).</summary>
public sealed class UnsupportedMediaTypeException(string message) : Exception(message)
{
}
