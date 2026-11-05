namespace TextChunker.Abstractions;

/// <summary>Записчик чанков. stdout — только валидные записи, ошибки — в stderr.</summary>
public interface IChunkWriter
{
    void Write(Chunk chunk);
}
