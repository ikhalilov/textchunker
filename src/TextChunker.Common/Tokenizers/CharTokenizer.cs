namespace TextChunker.Tokenizers;

using TextChunker.Abstractions;

/// <summary>
/// Tokenization by code points (runes): a surrogate pair is 1 token;
/// emoji sequences (ZWJ/families, flags, skin tones) are 1 token.
/// An empty string is 0 tokens.
/// <see cref="CutAtTokenBoundary"/> returns the UTF-16 CharIndex of the start of the
/// <c>n</c>-th token (0-based); if n == CountTokens(text) —
/// it returns the text length (a cut after all tokens).
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
                // the current cp joins the already started token — the count is not incremented.
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

        // Find the UTF-16 offset of the start of the n-th token (0-based).
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
                // cp joins the current token; the boundary is unchanged.
            }

            started = true;
            prevEndCp = cp;
            i += len;
        }

        return text.Length;
    }

    /// <summary>
    /// Reading a single code point: a surrogate pair → 1 (cp, len=2);
    /// a lone low surrogate → (char, 1).
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
        // 1. ZWJ (U+200D) always joins tokens.
        if (cur == 0x200D)
        {
            return true;
        }

        // 2. The token following a ZWJ joins the same sequence.
        if (prev == 0x200D)
        {
            return true;
        }

        // 3. Skin tones (U+1F3FB..U+1F3FF) join an emoji.
        if (IsSkinTone(cur) && IsEmoji(prev))
        {
            return true;
        }

        // 4. A flag (regional indicator) joins an emoji/flag.
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
