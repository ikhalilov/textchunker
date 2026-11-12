namespace TextChunker.Tokenizers;

using TextChunker.Abstractions;

/// <summary>
/// Токенизатор «word»: токен = максимальный отрезок не-пробельных символов
/// (напросток — "word count", `awk '{print NF}'`). Точный подсчёт слов,
/// но приближённый к токенам модели; в CLI с ним выдаётся warning «approximate».
/// Пустой/пробельный текст — 0 токенов.
/// Граница — UTF-16 CharIndex начала n-го слова (0-based); n >= count → текст.Length.
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
