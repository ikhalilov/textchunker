namespace TextChunker.Tokenizers;

using TextChunker.Abstractions;

/// <summary>
/// Токенизатор «none»: не считает токены вовсе (CountTokens = 0),
/// т.е. токенные лимиты не действуют. Граница — только начало/конец текста.
/// </summary>
public sealed class NullTokenizer : ITokenizer
{
    public string Name => "none";

    public string? ModelName => null;

    public int CountTokens(string text) => 0;

    public int? CutAtTokenBoundary(string text, int n)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        if (n <= 0)
        {
            return 0;
        }
        return text.Length;
    }
}
