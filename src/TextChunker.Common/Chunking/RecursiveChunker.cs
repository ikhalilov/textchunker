using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>
/// Recursive chunking over a hierarchical separator list (<see cref="ChunkingOptions.Separators"/>,
/// from coarse to fine). A part that is too large is split by the next separator or split hard.
/// </summary>
public sealed class RecursiveChunker : IChunker
{
    public ChunkSplit Chunk(string text, ITokenizer tokenizer, ChunkingOptions options, bool final = false)
    {
        return SeparatorChunker.Chunk(text, tokenizer, options, options.Separators, final);
    }
}
