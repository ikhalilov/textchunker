using System.Text;

using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>
/// Измерение размера текста в активной единице. Для токенов учитывает task-префикс:
/// лимит применяется к <c>prefix + text</c> (см. chunker-chunking / Task prefix).
/// </summary>
internal sealed class SizeMeasure
{
    private readonly ITokenizer _tokenizer;
    private readonly string _prefix;

    public SizeMeasure(ITokenizer tokenizer, SizeUnit unit, string? prefix)
    {
        _tokenizer = tokenizer;
        Unit = unit;
        _prefix = string.IsNullOrEmpty(prefix) ? string.Empty : prefix;
        PrefixTokens = unit == SizeUnit.Tokens && _prefix.Length > 0
            ? tokenizer.CountTokens(_prefix)
            : 0;
    }

    public SizeUnit Unit { get; }

    /// <summary>Число токенов префикса (0 для не-токенных единиц).</summary>
    public int PrefixTokens { get; }

    /// <summary>Размер текста в активной единице (с префиксом для токенов).</summary>
    public int Count(string text)
    {
        return Unit switch
        {
            SizeUnit.Tokens => _prefix.Length == 0
                ? _tokenizer.CountTokens(text)
                : _tokenizer.CountTokens(_prefix + text),
            SizeUnit.Chars => text.Length,
            SizeUnit.Words => CountWords(text),
            SizeUnit.Bytes => Encoding.UTF8.GetByteCount(text),
            _ => text.Length
        };
    }

    /// <summary>
    /// Наибольший индекс (exclusive) в [<paramref name="start"/>..text.Length], при котором
    /// размер <c>text[start..idx]</c> не превышает <paramref name="max"/>.
    /// Возвращает <paramref name="start"/>, если не помещается ни одна единица.
    /// </summary>
    public int MaxCut(string text, int start, int max)
    {
        if (max <= 0 || start >= text.Length)
        {
            return start;
        }

        return Unit switch
        {
            SizeUnit.Tokens => MaxCutTokens(text, start, max),
            SizeUnit.Chars => SafeCharCut(text, start, (int)Math.Min((long)start + max, text.Length)),
            SizeUnit.Words => MaxCutWords(text, start, max),
            SizeUnit.Bytes => MaxCutBytes(text, start, max),
            _ => SafeCharCut(text, start, (int)Math.Min((long)start + max, text.Length))
        };
    }

    /// <summary>Сдвиг на один код-поинт вперёд (не разрывая суррогатную пару).</summary>
    public static int NextCodePoint(string text, int index, int limit)
    {
        if (index >= limit)
        {
            return limit;
        }
        int next = index + 1;
        if (char.IsHighSurrogate(text[index]) && next < limit && char.IsLowSurrogate(text[next]))
        {
            next++;
        }
        return next;
    }

    internal static int CountWords(string text)
    {
        int count = 0;
        bool inWord = false;
        foreach (char c in text)
        {
            if (char.IsWhiteSpace(c))
            {
                inWord = false;
            }
            else if (!inWord)
            {
                count++;
                inWord = true;
            }
        }
        return count;
    }

    private int MaxCutTokens(string text, int start, int max)
    {
        int allowed = max - PrefixTokens;
        if (allowed <= 0)
        {
            return start;
        }

        string segment = text.Substring(start);
        if (_tokenizer.CountTokens(segment) <= allowed)
        {
            return text.Length;
        }

        // CutAtTokenBoundary 0-based: allowed — число токенов, которые оставляем.
        int tokensToKeep = allowed;
        while (tokensToKeep > 0)
        {
            int relative = _tokenizer.CutAtTokenBoundary(segment, tokensToKeep) ?? 0;
            if (relative <= 0)
            {
                return start;
            }
            if (relative > segment.Length)
            {
                relative = segment.Length;
            }

            // Граница склейки prefix+text может добавить токен — проверяем фактический размер.
            if (Count(segment.Substring(0, relative)) <= max)
            {
                return start + relative;
            }
            tokensToKeep--;
        }
        return start;
    }

    private static int MaxCutWords(string text, int start, int max)
    {
        int count = 0;
        bool inWord = false;
        for (int i = start; i < text.Length; i++)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                inWord = false;
                continue;
            }

            if (!inWord)
            {
                if (count == max)
                {
                    return i;
                }
                count++;
                inWord = true;
            }
        }
        return text.Length;
    }

    private static int MaxCutBytes(string text, int start, int max)
    {
        int lo = start;
        int hi = text.Length;
        while (lo < hi)
        {
            int mid = lo + (hi - lo + 1) / 2;
            int bytes = Encoding.UTF8.GetByteCount(text.AsSpan(start, mid - start));
            if (bytes <= max)
            {
                lo = mid;
            }
            else
            {
                hi = mid - 1;
            }
        }
        return SafeCharCut(text, start, lo);
    }

    /// <summary>Не разрешаем резу между high- и low-surrogate.</summary>
    private static int SafeCharCut(string text, int start, int cut)
    {
        if (cut > text.Length)
        {
            cut = text.Length;
        }
        if (cut > start && cut < text.Length
            && char.IsLowSurrogate(text[cut]) && char.IsHighSurrogate(text[cut - 1]))
        {
            cut--;
        }
        return cut;
    }
}
