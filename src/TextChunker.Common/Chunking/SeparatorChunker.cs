using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>
/// Общий движок иерархической нарезки по разделителям (recursive/paragraph):
/// рекурсивно дробит фрагмент разделителями, затем жадно упаковывает части
/// в чанки не больше лимита. Не гарантирует разрыв только по разделителю —
/// слишком крупная часть дробится жёстко (fallback).
/// </summary>
internal static class SeparatorChunker
{
    private readonly record struct Piece(int Start, int End);

    public static ChunkSplit Chunk(
        string text,
        ITokenizer tokenizer,
        ChunkingOptions options,
        IReadOnlyList<string> separators,
        bool final)
    {
        if (string.IsNullOrEmpty(text))
        {
            return ChunkSplit.Empty;
        }

        var measure = new SizeMeasure(tokenizer, options.Unit, options.Prefix);
        int max = options.MaxSize ?? int.MaxValue;
        int overlap = ChunkerSupport.EffectiveOverlap(options);

        var pieces = new List<Piece>();
        Split(text, 0, text.Length, separators, 0, max, measure, pieces);

        var segments = new List<ChunkSegment>();
        int accStart = -1;
        int accEnd = -1;

        foreach (Piece piece in pieces)
        {
            if (ChunkerSupport.IsWhitespaceOnly(text, piece.Start, piece.End))
            {
                continue;
            }

            if (accStart < 0)
            {
                accStart = piece.Start;
                accEnd = piece.End;
                continue;
            }

            if (measure.Count(text.Substring(accStart, piece.End - accStart)) <= max)
            {
                accEnd = piece.End;
            }
            else
            {
                ChunkerSupport.AddSegment(segments, text, accStart, accEnd, options.Trim);

                int nextStart = piece.Start;
                if (overlap > 0)
                {
                    int overlapStart = ChunkerSupport.OverlapStart(
                        tokenizer, text, accStart, accEnd, overlap, options.Unit);
                    if (overlapStart > accStart
                        && overlapStart < nextStart
                        && measure.Count(text[overlapStart..piece.End]) <= max)
                    {
                        nextStart = overlapStart;
                    }
                }

                accStart = nextStart;
                accEnd = piece.End;
            }
        }

        if (accStart < 0)
        {
            return new ChunkSplit
            {
                Segments = segments,
                ConsumedEnd = text.Length,
                MoreNeeded = false
            };
        }

        bool endsWithBoundary = char.IsWhiteSpace(text[text.Length - 1]);
        bool mayContinue = accEnd == text.Length && !endsWithBoundary;
        int consumed;
        bool moreNeeded;

        if (!final && mayContinue)
        {
            // Последняя часть может продолжиться следующим фрагментом — не выдаём её.
            consumed = RetainOverlapStart(text, options, tokenizer, segments, overlap, accStart);
            moreNeeded = true;
        }
        else
        {
            ChunkerSupport.AddSegment(segments, text, accStart, accEnd, options.Trim);
            consumed = final
                ? text.Length
                : RetainOverlapStart(text, options, tokenizer, segments, overlap, text.Length);
            moreNeeded = false;
        }

        return new ChunkSplit
        {
            Segments = segments,
            ConsumedEnd = consumed,
            MoreNeeded = moreNeeded
        };
    }

    /// <summary>
    /// Если перекрытие включено и уже есть выданные сегменты, оставляем в буфере хвост
    /// последнего чанка (следующий чанк начнётся с него). Иначе — <paramref name="fallback"/>.
    /// </summary>
    private static int RetainOverlapStart(
        string text,
        ChunkingOptions options,
        ITokenizer tokenizer,
        List<ChunkSegment> emitted,
        int overlap,
        int fallback)
    {
        if (overlap <= 0 || emitted.Count == 0)
        {
            return fallback;
        }

        ChunkSegment last = emitted[emitted.Count - 1];
        int overlapStart = ChunkerSupport.OverlapStart(
            tokenizer, text, last.StartInText, last.EndInText, overlap, options.Unit);

        if (overlapStart > last.StartInText && overlapStart < fallback)
        {
            return overlapStart;
        }
        return fallback;
    }

    private static void Split(
        string text,
        int start,
        int end,
        IReadOnlyList<string> separators,
        int level,
        int max,
        SizeMeasure measure,
        List<Piece> pieces)
    {
        if (end <= start)
        {
            return;
        }

        if (measure.Count(text.Substring(start, end - start)) <= max)
        {
            pieces.Add(new Piece(start, end));
            return;
        }

        if (level >= separators.Count)
        {
            HardSplit(text, start, end, max, measure, pieces);
            return;
        }

        string separator = separators[level];
        if (separator.Length == 0)
        {
            HardSplit(text, start, end, max, measure, pieces);
            return;
        }

        int pos = start;
        bool found = false;
        while (pos < end)
        {
            int index = text.IndexOf(separator, pos, end - pos, StringComparison.Ordinal);
            if (index < 0)
            {
                break;
            }

            found = true;
            int partEnd = index + separator.Length;
            Split(text, pos, partEnd, separators, level + 1, max, measure, pieces);
            pos = partEnd;
        }

        if (!found)
        {
            Split(text, start, end, separators, level + 1, max, measure, pieces);
            return;
        }

        if (pos < end)
        {
            Split(text, pos, end, separators, level + 1, max, measure, pieces);
        }
    }

    private static void HardSplit(
        string text,
        int start,
        int end,
        int max,
        SizeMeasure measure,
        List<Piece> pieces)
    {
        int pos = start;
        while (pos < end)
        {
            int cut = measure.MaxCut(text, pos, max);
            if (cut <= pos)
            {
                cut = SizeMeasure.NextCodePoint(text, pos, end);
            }
            if (cut > end)
            {
                cut = end;
            }
            pieces.Add(new Piece(pos, cut));
            pos = cut;
        }
    }
}
