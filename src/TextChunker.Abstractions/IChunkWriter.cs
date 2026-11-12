namespace TextChunker.Abstractions;

/// <summary>Chunk writer. stdout carries only valid records, errors go to stderr.</summary>
public interface IChunkWriter
{
    void Write(Chunk chunk);
}
