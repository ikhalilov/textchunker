namespace TextChunker.Tokenizers;

using TextChunker.Abstractions;

/// <summary>
/// The "word" tokenizer: a token is the longest run of non-whitespace characters
/// (simply put — "word count", `awk '{print NF}'`). It counts words exactly,
/// but only approximates model tokens; the CLI reports an "approximate" warning for it.
/// Empty/whitespace-only text is 0 tokens.
/// A boundary is the UTF-16 CharIndex of the start of the n-th word (0-based); n >= count → text.Length.
/// </summary>
public sealed class WordTokenizer : ITokenizer
{
    public string Name => "word";

    public string? ModelName => null;

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        int count = 0;
        bool inWord = false;
        for (int i = 0; i < text.Length; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                if (!inWord)
                {
                    count++;
                    inWord = true;
                }
            }
            else
            {
                inWord = false;
            }
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

        int current = 0;
        int i = 0;
        int wordStart = 0;
        while (i < text.Length)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                wordStart = i;
                int end = i;
                while (end < text.Length && !char.IsWhiteSpace(text[end]))
                {
                    end++;
                }
                if (current == n)
                {
                    return wordStart;
                }
                current++;
                i = end;
            }
            else
            {
                i++;
            }
        }
        return text.Length;
    }
}
