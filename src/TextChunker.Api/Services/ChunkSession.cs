using TextChunker.Chunking;

namespace TextChunker.Api.Services;

/// <summary>Chunking session: the service and the text source for an HTTP request.</summary>
/// <param name="Service">The chunking service.</param>
/// <param name="Source">The in-memory text source.</param>
public sealed record ChunkSession(ChunkingService Service, InMemoryTextSource Source);
