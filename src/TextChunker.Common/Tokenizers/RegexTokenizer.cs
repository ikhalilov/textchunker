namespace TextChunker.Tokenizers;

using System.Text.RegularExpressions;

using TextChunker.Abstractions;

/// <summary>
/// Токенизатор «regex»: токены = совпадения регулярного выражения.
/// По умолчанию — последовательности Unicode-букв и цифр, т.е. <c>[\p{L}\p{N}]</c>+ (слова без знаков препинания/пробелов).
/// Граница — CharIndex начала n-го совпадения (0-based); n >= count → text.Length.
/// </summary>
public sealed class RegexTokenizer : ITokenizer
{
    /// <summary>По умолчанию: последовательности Unicode-букв и цифр.</summary>
    public const string DefaultPattern = @"[\p{L}\p{N}]+";

    private readonly Regex _regex;

    public RegexTokenizer() : this(DefaultPattern) { }

    public RegexTokenizer(string pattern)
    {
        if (string.IsNullOrWhiteSpace(pattern))
        {
            throw new ArgumentException("Паттерн не может быть пустым", nameof(pattern));
        }
        _regex = new Regex(pattern, RegexOptions.CultureInvariant);
    }

    public string Name => "regex";

    public string? ModelName => null;

    public int CountTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }
        return _regex.Matches(text).Count;
    }

    public int? CutAtTokenBoundary(string text, int n)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        MatchCollection matches = _regex.Matches(text);
        if (n <= 0)
        {
            return 0;
        }
        if (n >= matches.Count)
        {
            return text.Length;
        }
        return matches[n].Index;
    }
}
