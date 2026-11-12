using System.Text.Json.Serialization;

namespace TextChunker.Api.Contracts;

/// <summary>Supported tokenizers.</summary>
/// <param name="Names">Allowed <c>tokenizer</c> values.</param>
/// <param name="DefaultModel">Default HuggingFace model for <c>tokenizer=huggingface</c>.</param>
public sealed record TokenizersResponse(
    [property: JsonPropertyName("names")] IReadOnlyList<string> Names,
    [property: JsonPropertyName("defaultModel")] string DefaultModel);
