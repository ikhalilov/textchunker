using System.Text.Json.Serialization;

namespace TextChunker.Api.Contracts;

/// <summary>Non-streaming chunking response: all chunks and aggregated statistics.</summary>
/// <param name="Chunks">List of chunks.</param>
/// <param name="Stats">Aggregated statistics.</param>
public sealed record ChunkResponse(
    [property: JsonPropertyName("chunks")] IReadOnlyList<Abstractions.Chunk> Chunks,
    [property: JsonPropertyName("stats")] Chunking.ChunkingStats Stats);
