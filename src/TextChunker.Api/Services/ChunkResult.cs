using TextChunker.Abstractions;
using TextChunker.Chunking;

namespace TextChunker.Api.Services;

/// <summary>Chunking result: the chunks and aggregated statistics.</summary>
/// <param name="Chunks">The list of chunks.</param>
/// <param name="Stats">Aggregated statistics.</param>
public sealed record ChunkResult(IReadOnlyList<Chunk> Chunks, ChunkingStats Stats);
