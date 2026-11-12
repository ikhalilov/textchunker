using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>
/// Нарезка по абзацам: сначала разбиение по пустым строкам (<c>\n\n</c>), затем
/// жадное объединение малых абзацев до лимита и суб-разбивка крупных абзацев
/// оставшимися разделителями (recursive/hard fallback).
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
