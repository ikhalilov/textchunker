using TextChunker.Abstractions;

namespace TextChunker.Tests;

internal sealed class CollectingWriter : IChunkWriter
{
    public List<Chunk> Chunks { get; } = [];

    public void Write(Chunk chunk) => Chunks.Add(chunk);

    public string[] Texts => [.. Chunks.Select(c => c.Text)];
}
