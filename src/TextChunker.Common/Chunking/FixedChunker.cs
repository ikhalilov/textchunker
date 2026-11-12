using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>
/// Фиксированная нарезка: каждый чанк — максимальный префикс, укладывающийся в лимит
/// активной единицы; следующий чанк начинается через <c>overlap</c> единиц от конца
/// предыдущего. Если остаток фрагмента помещается в лимит и это не EOF, чанк
/// не выдаётся — нужно больше текста (потоковый режим).
/// </summary>
public sealed class FixedChunker : IChunker
{
    public ChunkSplit Chunk(string text, ITokenizer tokenizer, ChunkingOptions options, bool final = false)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ChunkSplit.Empty;
        }

        var measure = new SizeMeasure(tokenizer, options.Unit, options.Prefix);
        int max = options.MaxSize ?? int.MaxValue;
        int overlap = ChunkerSupport.EffectiveOverlap(options);

        var segments = new List<ChunkSegment>();
        int pos = 0;

        while (pos < text.Length)
        {
            int start = ChunkerSupport.SkipWhitespace(text, pos, options.Trim);
            if (start >= text.Length)
            {
                pos = text.Length;
                break;
            }

            int cut = measure.MaxCut(text, start, max);
            if (cut <= start)
            {
                cut = SizeMeasure.NextCodePoint(text, start, text.Length);
            }

            bool reachesEnd = cut >= text.Length;
            if (reachesEnd && !final)
            {
                // Остаток помещается в лимит: возможно, следующие строки его продолжат.
                return new ChunkSplit
                {
                    Segments = segments,
                    ConsumedEnd = start,
                    MoreNeeded = true
                };
            }

            ChunkerSupport.AddSegment(segments, text, start, cut, options.Trim);
            pos = cut;

            if (!reachesEnd && overlap > 0)
            {
                int overlapStart = ChunkerSupport.OverlapStart(tokenizer, text, start, cut, overlap, options.Unit);
                if (overlapStart > start && overlapStart < cut)
                {
                    pos = overlapStart;
                }
            }
        }

        return new ChunkSplit
        {
            Segments = segments,
            ConsumedEnd = pos,
            MoreNeeded = false
        };
    }
}
