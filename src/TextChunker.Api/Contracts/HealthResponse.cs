using System.Text.Json.Serialization;

namespace TextChunker.Api.Contracts;

/// <summary>Service health.</summary>
/// <param name="Status">Always <c>ok</c> while the service is responding.</param>
/// <param name="Version">API assembly version.</param>
public sealed record HealthResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("version")] string Version);
