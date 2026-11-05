using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>Общие операции нарезки: обрезка пробелов, эмиссия сегментов, перекрытие.</summary>
internal static class ChunkerSupport
{
    public static int SkipWhitespace(string text, int index, bool trim)
    {
        if (!trim)
        {
            return index;
        }
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
        return index;
    }

    public static bool IsWhitespaceOnly(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Добавить сегмент [start,end) с обрезкой пробелов по краям (если trim).</summary>
    public static void AddSegment(List<ChunkSegment> segments, string text, int start, int end, bool trim)
    {
        if (trim)
        {
            while (start < end && char.IsWhiteSpace(text[start]))
            {
                start++;
            }
            while (end > start && char.IsWhiteSpace(text[end - 1]))
            {
                end--;
            }
        }

        if (end <= start)
        {
            return;
        }

        segments.Add(new ChunkSegment(text.Substring(start, end - start), start, end));
    }

    /// <summary>Перекрытие в единицах активного измерения (0 для words/bytes).</summary>
    public static int EffectiveOverlap(ChunkingOptions options)
    {
        return options.Unit switch
        {
            SizeUnit.Tokens => options.OverlapTokens ?? 0,
            SizeUnit.Chars => options.OverlapChars ?? 0,
            _ => 0
        };
    }

    /// <summary>
    /// Начало перекрытия: CharIndex внутри только что выданного чанка [start,end),
    /// с которого должен начаться следующий чанк. Возвращает end, если перекрытие
    /// невозможно/не даёт прогресса.
    /// </summary>
    public static int OverlapStart(
        ITokenizer tokenizer,
        string text,
        int start,
        int end,
        int overlap,
        SizeUnit unit)
    {
        if (overlap <= 0 || end <= start)
        {
            return end;
        }

        if (unit == SizeUnit.Chars)
        {
            int byChars = end - overlap;
            return byChars > start ? byChars : end;
        }

        if (unit == SizeUnit.Tokens)
        {
            string chunk = text.Substring(start, end - start);
            int tokens = tokenizer.CountTokens(chunk);
            int keep = tokens - overlap;
            if (keep <= 0)
            {
                return end;
            }
            int relative = tokenizer.CutAtTokenBoundary(chunk, keep) ?? 0;
            int result = start + relative;
            return result > start && result < end ? result : end;
        }

        return end;
    }
}
