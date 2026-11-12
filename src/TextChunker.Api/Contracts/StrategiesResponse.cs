using System.Text.Json.Serialization;

namespace TextChunker.Api.Contracts;

/// <summary>Supported chunking strategies.</summary>
/// <param name="Names">Allowed <c>strategy</c> values.</param>
public sealed record StrategiesResponse(
    [property: JsonPropertyName("names")] IReadOnlyList<string> Names);
