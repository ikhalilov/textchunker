namespace TextChunker.Tokenizers;

using TextChunker.Abstractions;

/// <summary>
/// Токенизация по кодовым точкам (runes): пара суррогатов — 1 токен;
/// эмодзи-цепочки (ZWJ/семьи, флаги, оттенки кожи) — 1 токен.
/// Пустая строка — 0 токенов.
/// <see cref="CutAtTokenBoundary"/> возвращает UTF-16 CharIndex начала
/// <c>n</c>-го токена (0-based); если n == CountTokens(text) —
/// возвращает длину текста (рез после всех токенов).
/// </summary>
public sealed class CharTokenizer : ITokenizer
{
    public string Name => "char";

    public string? ModelName => null;

    public int CountTokens(string text)
    {
        int count = 0;
        int i = 0;
        int prevEndCp = 0;
        bool started = false;
        while (i < text.Length)
        {
            int cp = ReadCodePoint(text, i, out int len);
            if (started && ShouldJoin(prevEndCp, cp))
            {
                // текущее cp входит в уже начатый токен — не увеличиваем счёт.
            }
            else
            {
                count++;
            }

            started = true;
            prevEndCp = cp;
            i += len;
        }
        return count;
    }

    public int? CutAtTokenBoundary(string text, int n)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        int total = CountTokens(text);
        if (n <= 0)
        {
            return 0;
        }
        if (n >= total)
        {
            return text.Length;
        }

        // Найдём UTF-16 смещение начала n-го токена (0-based).
        int unitIndex = 0;
        int i = 0;
        int unitStart = 0;
        int prevEndCp = 0;
        bool started = false;

        while (i < text.Length)
        {
            int cp = ReadCodePoint(text, i, out int len);
            bool join = started && ShouldJoin(prevEndCp, cp);
            if (!join)
            {
                unitStart = i;
                if (unitIndex == n)
                {
                    return unitStart;
                }
                unitIndex++;
            }
            else
            {
                // cp входит в текущий токен; граница не меняется.
            }

            started = true;
            prevEndCp = cp;
            i += len;
        }

        return text.Length;
    }

    /// <summary>
    /// Чтение одного кодового пункта: пара суррогатов → 1 (cp, len=2);
    /// одинокий low-surrogate → (char, 1).
    /// </summary>
    private static int ReadCodePoint(string text, int i, out int len)
    {
        char ch = text[i];
        if (char.IsHighSurrogate(ch) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
        {
            len = 2;
            int cp = 0x10000 + ((ch - 0xD800) << 10) + (text[i + 1] - 0xDC00);
            return cp;
        }

        len = 1;
        return ch;
    }

    private static bool ShouldJoin(int prev, int cur)
    {
        // 1. ZWJ (U+200D) всегда объединяет токены.
        if (cur == 0x200D)
        {
            return true;
        }

        // 2. Токен, следующий за ZWJ, входит в ту же цепочку.
        if (prev == 0x200D)
        {
            return true;
        }

        // 3. Оттенки кожи (U+1F3FB..U+1F3FF) к эмодзи.
        if (IsSkinTone(cur) && IsEmoji(prev))
        {
            return true;
        }

        // 4. Флаг (regional indicator) к эмодзи/флагу.
        if (IsFlag(cur) && (IsFlag(prev) || IsEmoji(prev)))
        {
            return true;
        }

        return false;
    }

    private static bool IsSkinTone(int cp) => cp >= 0x1F3FB && cp <= 0x1F3FF;

    private static bool IsFlag(int cp) => cp >= 0x1F1E6 && cp <= 0x1F1FF;

    private static bool IsEmoji(int cp) =>
        (cp >= 0x1F300 && cp <= 0x1F3FF) ||
        (cp >= 0x1F400 && cp <= 0x1F6FF) ||
        (cp >= 0x1F900 && cp <= 0x1FAFF) ||
        (cp >= 0x1F000 && cp <= 0x1F0FF) ||
        (cp >= 0x2600 && cp <= 0x26FF) ||
        (cp >= 0x2700 && cp <= 0x27BF);
}
