namespace TextChunker.Api.Services;

/// <summary>Неподдерживаемый тип загружаемого файла (415).</summary>
public sealed class UnsupportedMediaTypeException(string message) : Exception(message)
{
}
