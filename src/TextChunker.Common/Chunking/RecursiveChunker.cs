using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>
/// Рекурсивная нарезка по иерархическому списку разделителей (<see cref="ChunkingOptions.Separators"/>,
/// от крупного к мелкому). Слишком крупная часть дробится следующим разделителем или жёстко.
/// </summary>
public sealed class RecursiveChunker : IChunker
{
    public ChunkSplit Chunk(string text, ITokenizer tokenizer, ChunkingOptions options, bool final = false)
    {
        return SeparatorChunker.Chunk(text, tokenizer, options, options.Separators, final);
    }
}
