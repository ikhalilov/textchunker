using System.Text.Json.Serialization;

namespace TextChunker.Api.Contracts;

/// <summary>Поддерживаемые стратегии нарезки.</summary>
/// <param name="Names">Допустимые значения <c>strategy</c>.</param>
public sealed record StrategiesResponse(
    [property: JsonPropertyName("names")] IReadOnlyList<string> Names);
