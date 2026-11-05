using System.Text.Json.Serialization;

namespace TextChunker.Api.Contracts;

/// <summary>Ответ не-потоковой нарезки: все чанки и агрегированная статистика.</summary>
/// <param name="Chunks">Список чанков.</param>
/// <param name="Stats">Агрегированная статистика.</param>
public sealed record ChunkResponse(
    [property: JsonPropertyName("chunks")] IReadOnlyList<Abstractions.Chunk> Chunks,
    [property: JsonPropertyName("stats")] Chunking.ChunkingStats Stats);
