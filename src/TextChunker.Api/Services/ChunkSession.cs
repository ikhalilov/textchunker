using TextChunker.Chunking;

namespace TextChunker.Api.Services;

/// <summary>Сессия нарезки: сервис и источник текста для HTTP-запроса.</summary>
/// <param name="Service">Сервис нарезки.</param>
/// <param name="Source">In-memory источник текста.</param>
public sealed record ChunkSession(ChunkingService Service, InMemoryTextSource Source);
