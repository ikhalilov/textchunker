namespace TextChunker.Abstractions;

/// <summary>Chunk segment within the incoming text: (text, start, end) — CharIndex in `text`.</summary>
public record ChunkSegment(string Text, int StartInText, int EndInText);

public interface IChunker
{
    /// <summary>
    /// Split <paramref name="text"/> (a fragment, accounting for startChar/offset) into chunks:
    /// a greedy algorithm with overlap, splitting on separators, and a final token recount by the service.
    /// <paramref name="final"/> = true — the input has ended, and the remainder must be emitted as the last chunk.
    /// <see cref="ChunkSplit.MoreNeeded"/> = true if the fragment ended inside a potential chunk
    /// (reading must continue so that no partial hard cut is emitted).
    /// </summary>
    ChunkSplit Chunk(string text, ITokenizer tokenizer, ChunkingOptions options, bool final = false);
}
