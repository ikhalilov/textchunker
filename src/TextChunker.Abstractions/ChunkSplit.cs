namespace TextChunker.Abstractions;

/// <summary>Result of splitting a text fragment.</summary>
public sealed record ChunkSplit
{
    public required IReadOnlyList<ChunkSegment> Segments { get; init; }
    /// <summary>End of the last emitted chunk (CharIndex in the source text, inclusive). 0 when there are no chunks.</summary>
    public int ConsumedEnd { get; init; }
    /// <summary>Whether more text must be read to produce the next chunk.</summary>
    public bool MoreNeeded { get; init; }

    public static ChunkSplit Empty => new() { Segments = [], ConsumedEnd = 0, MoreNeeded = false };

    public static ChunkSplit NeedMore => new() { Segments = [], ConsumedEnd = 0, MoreNeeded = true };
}
