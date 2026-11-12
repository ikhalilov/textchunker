using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>Collects the chunks of a window in memory for filtering/renumbering.</summary>
internal sealed class ListChunkWriter : IChunkWriter
{
    public List<Chunk> Chunks { get; } = [];

    public void Write(Chunk chunk) => Chunks.Add(chunk);
}
