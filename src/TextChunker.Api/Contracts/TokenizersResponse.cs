using System.Text.Json.Serialization;

namespace TextChunker.Api.Contracts;

/// <summary>Поддерживаемые токенизаторы.</summary>
/// <param name="Names">Допустимые значения <c>tokenizer</c>.</param>
/// <param name="DefaultModel">Модель HuggingFace по умолчанию для <c>tokenizer=huggingface</c>.</param>
public sealed record TokenizersResponse(
    [property: JsonPropertyName("names")] IReadOnlyList<string> Names,
    [property: JsonPropertyName("defaultModel")] string DefaultModel);
