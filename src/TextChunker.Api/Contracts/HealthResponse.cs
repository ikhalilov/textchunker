using System.Text.Json.Serialization;

namespace TextChunker.Api.Contracts;

/// <summary>Состояние сервиса.</summary>
/// <param name="Status">Всегда <c>ok</c>, если сервис отвечает.</param>
/// <param name="Version">Версия сборки API.</param>
public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] string Version);
