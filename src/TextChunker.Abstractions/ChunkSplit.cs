namespace TextChunker.Abstractions;

/// <summary>Результат разрезки фрагмента текста.</summary>
public sealed record ChunkSplit
{
    public required IReadOnlyList<ChunkSegment> Segments { get; init; }
    /// <summary>Конец последнего выданного чанка (CharIndex в исходном тексте, включительно). 0, если чанков нет.</summary>
    public int ConsumedEnd { get; init; }
    /// <summary>Нужно ли продолжать чтение текста для следующего чанка.</summary>
    public bool MoreNeeded { get; init; }

    public static ChunkSplit Empty => new() { Segments = [], ConsumedEnd = 0, MoreNeeded = false };

    public static ChunkSplit NeedMore => new() { Segments = [], ConsumedEnd = 0, MoreNeeded = true };
}
