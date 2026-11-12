namespace TextChunker.Tokenizers;

using TextChunker.Abstractions;

/// <summary>
/// The "none" tokenizer: it does not count tokens at all (CountTokens = 0),
/// i.e. token limits do not apply. A boundary is only the start/end of the text.
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
