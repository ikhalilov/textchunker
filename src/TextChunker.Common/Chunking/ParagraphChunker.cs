using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>
/// Paragraph chunking: first split on blank lines (<c>\n\n</c>), then
/// greedily merge small paragraphs up to the limit and split large paragraphs further
/// with the remaining separators (recursive/hard fallback).
/// </summary>
public sealed class ParagraphChunker : IChunker
{
    public ChunkSplit Chunk(string text, ITokenizer tokenizer, ChunkingOptions options, bool final = false)
    {
        var separators = new List<string> { "\n\n" };
        foreach (string separator in options.Separators)
        {
            if (separator != "\n\n")
            {
                separators.Add(separator);
            }
        }

        return SeparatorChunker.Chunk(text, tokenizer, options, separators, final);
    }
}
