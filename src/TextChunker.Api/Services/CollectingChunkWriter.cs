using TextChunker.Abstractions;

namespace TextChunker.Api.Services;

/// <summary>Собирает чанки в память (не-потоковый endpoint).</summary>
public sealed class CollectingChunkWriter : IChunkWriter
{
    public List<Chunk> Chunks { get; } = [];

    public void Write(Chunk chunk) => Chunks.Add(chunk);
}
