using TextChunker.Abstractions;

namespace TextChunker.Api.Services;

/// <summary>Collects chunks into memory (non-streaming endpoint).</summary>
public sealed class CollectingChunkWriter : IChunkWriter
{
    public List<Chunk> Chunks { get; } = [];

    public void Write(Chunk chunk) => Chunks.Add(chunk);
}
