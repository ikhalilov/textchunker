using TextChunker.Abstractions;
using TextChunker.Chunking;

namespace TextChunker.Api.Services;

/// <summary>Результат нарезки: чанки и агрегированная статистика.</summary>
/// <param name="Chunks">Список чанков.</param>
/// <param name="Stats">Агрегированная статистика.</param>
public sealed record ChunkResult(IReadOnlyList<Chunk> Chunks, ChunkingStats Stats);
