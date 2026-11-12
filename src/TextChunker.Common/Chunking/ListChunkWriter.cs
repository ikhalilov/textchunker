using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>Собирает чанки окна в память для фильтрации/перенумерации.</summary>
internal sealed class ListChunkWriter : IChunkWriter
{
    public List<Chunk> Chunks { get; } = [];

    public void Write(Chunk chunk) => Chunks.Add(chunk);
}
