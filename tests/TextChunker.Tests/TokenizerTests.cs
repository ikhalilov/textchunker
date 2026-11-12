using TextChunker.Abstractions;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

/// <summary>Unit tests for the basic tokenizers: char (rune/emoji), word, regex, none.</summary>
public sealed class CharTokenizerTests
{
    private readonly CharTokenizer _tokenizer = new();

    [Fact]
    public void EmptyTextHasNoTokens() => Assert.Equal(0, _tokenizer.CountTokens(""));

    [Fact]
    public void AsciiCountsPerCodePoint() => Assert.Equal(3, _tokenizer.CountTokens("abc"));

    [Fact]
    public void CyrillicCountsPerCodePoint() => Assert.Equal(6, _tokenizer.CountTokens("Привет"));

    [Fact]
    public void SurrogatePairIsOneToken() => Assert.Equal(1, _tokenizer.CountTokens("\U0001D11E"));

    [Fact]
    public void ZwjEmojiFamilyIsOneToken()
    {
        const string family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        Assert.Equal(1, _tokenizer.CountTokens(family));
        Assert.Equal(3, _tokenizer.CountTokens("a" + family + "b"));
    }

    [Fact]
    public void FlagIsOneToken() => Assert.Equal(1, _tokenizer.CountTokens("\U0001F1F7\U0001F1FA"));

    [Fact]
    public void SkinToneModifierDoesNotAddToken()
    {
        Assert.Equal(1, _tokenizer.CountTokens("\U0001F44D\U0001F3FD"));
        Assert.Equal(2, _tokenizer.CountTokens("\U0001F44D\U0001F3FD\U0001F44D\U0001F3FD"));
    }

    [Fact]
    public void CutAtTokenBoundaryIsZeroBasedAndKeepsSurrogatesWhole()
    {
        // a + family(8 UTF-16 units) + b => tokens: 'a', family, 'b'
        const string text = "a\U0001F468\u200D\U0001F469\u200D\U0001F467b";
        Assert.Equal(0, _tokenizer.CutAtTokenBoundary(text, 0));
        Assert.Equal(1, _tokenizer.CutAtTokenBoundary(text, 1));
        Assert.Equal(9, _tokenizer.CutAtTokenBoundary(text, 2));
        Assert.Equal(text.Length, _tokenizer.CutAtTokenBoundary(text, 3));
        Assert.Equal(text.Length, _tokenizer.CutAtTokenBoundary(text, 99));
    }

    [Fact]
    public void CutOnEmptyTextIsNull() => Assert.Null(_tokenizer.CutAtTokenBoundary("", 1));

    [Fact]
    public void CountIsDeterministic()
    {
        const string text = "Привет, мир! \U0001F600";
        Assert.Equal(_tokenizer.CountTokens(text), _tokenizer.CountTokens(text));
    }
}

/// <summary>Unit tests for the word tokenizer (a token = a run of non-whitespace characters).</summary>
public sealed class WordTokenizerTests
{
    private readonly WordTokenizer _tokenizer = new();

    [Fact]
    public void EmptyAndWhitespaceHaveNoTokens()
    {
        Assert.Equal(0, _tokenizer.CountTokens(""));
        Assert.Equal(0, _tokenizer.CountTokens("   \t\r\n"));
    }

    [Fact]
    public void WordsAreSeparatedByWhitespace() => Assert.Equal(3, _tokenizer.CountTokens("one two three"));

    [Fact]
    public void PunctuationStaysInsideWord() => Assert.Equal(2, _tokenizer.CountTokens("Привет, мир!"));

    [Fact]
    public void TabsAndNewlinesSeparateWords() => Assert.Equal(3, _tokenizer.CountTokens("one\ntwo\tthree"));

    [Fact]
    public void CountIsDeterministic()
    {
        const string text = "alpha beta gamma";
        Assert.Equal(_tokenizer.CountTokens(text), _tokenizer.CountTokens(text));
    }

    [Fact]
    public void CutAtTokenBoundaryIsZeroBased()
    {
        const string text = "one two three";
        Assert.Equal(0, _tokenizer.CutAtTokenBoundary(text, 0));
        Assert.Equal(4, _tokenizer.CutAtTokenBoundary(text, 1));
        Assert.Equal(8, _tokenizer.CutAtTokenBoundary(text, 2));
        Assert.Equal(text.Length, _tokenizer.CutAtTokenBoundary(text, 3));
        Assert.Equal(text.Length, _tokenizer.CutAtTokenBoundary(text, 99));
    }

    [Fact]
    public void CutOnEmptyTextIsNull() => Assert.Null(_tokenizer.CutAtTokenBoundary("", 1));
}

/// <summary>Unit tests for the regex tokenizer (matches of `[\p{L}\p{N}]+`).</summary>
public sealed class RegexTokenizerTests
{
    private readonly RegexTokenizer _tokenizer = new();

    [Fact]
    public void EmptyTextHasNoTokens() => Assert.Equal(0, _tokenizer.CountTokens(""));

    [Fact]
    public void PunctuationOnlyHasNoTokens() => Assert.Equal(0, _tokenizer.CountTokens("!!! ... ,,"));

    [Fact]
    public void LatinAndCyrillicWordsAreCounted()
    {
        Assert.Equal(2, _tokenizer.CountTokens("hello, world!"));
        Assert.Equal(2, _tokenizer.CountTokens("Привет, мир"));
        Assert.Equal(2, _tokenizer.CountTokens("a1 b2"));
    }

    [Fact]
    public void CountIsDeterministic()
    {
        const string text = "one, two; three!";
        Assert.Equal(_tokenizer.CountTokens(text), _tokenizer.CountTokens(text));
    }

    [Fact]
    public void CutAtTokenBoundaryIsZeroBased()
    {
        const string text = "hello, world!";
        Assert.Equal(0, _tokenizer.CutAtTokenBoundary(text, 0));
        Assert.Equal(7, _tokenizer.CutAtTokenBoundary(text, 1));
        Assert.Equal(text.Length, _tokenizer.CutAtTokenBoundary(text, 2));
        Assert.Equal(text.Length, _tokenizer.CutAtTokenBoundary(text, 99));
    }

    [Fact]
    public void CutOnEmptyTextIsNull() => Assert.Null(_tokenizer.CutAtTokenBoundary("", 1));
}

/// <summary>Unit tests for the `none` tokenizer (the token limit is disabled).</summary>
public sealed class NullTokenizerTests
{
    private readonly NullTokenizer _tokenizer = new();

    [Fact]
    public void AnyTextHasZeroTokens() => Assert.Equal(0, _tokenizer.CountTokens("любой текст 🚀"));

    [Fact]
    public void BoundaryIsAlwaysStartOrEnd()
    {
        Assert.Equal(0, _tokenizer.CutAtTokenBoundary("abcd", 0));
        Assert.Equal(0, _tokenizer.CutAtTokenBoundary("abcd", -1));
        Assert.Equal(4, _tokenizer.CutAtTokenBoundary("abcd", 1));
    }

    [Fact]
    public void CutOnEmptyTextIsNull() => Assert.Null(_tokenizer.CutAtTokenBoundary("", 0));
}

/// <summary>Factory: names, case/spaces, unknown name, default value.</summary>
public sealed class TokenizerFactoryTests
{
    [Fact]
    public void MissingNameFallsBackToWord()
    {
        Assert.IsType<WordTokenizer>(TokenizerFactory.Create(null));
        Assert.IsType<WordTokenizer>(TokenizerFactory.Create(""));
        Assert.IsType<WordTokenizer>(TokenizerFactory.Create("   "));
        Assert.IsType<WordTokenizer>(TokenizerFactory.CreateDefault());
    }

    [Theory]
    [InlineData("none", typeof(NullTokenizer))]
    [InlineData("char", typeof(CharTokenizer))]
    [InlineData("word", typeof(WordTokenizer))]
    [InlineData("regex", typeof(RegexTokenizer))]
    [InlineData("CHAR", typeof(CharTokenizer))]
    [InlineData(" regex ", typeof(RegexTokenizer))]
    public void NamedTokenizers(string name, Type expected)
    {
        Assert.IsType(expected, TokenizerFactory.Create(name));
    }

    [Fact]
    public void SupportedNamesAreExposed()
    {
        Assert.Equal(
            new[] { "none", "char", "word", "regex", "huggingface" },
            TokenizerFactory.SupportedNames);
    }

    [Fact]
    public void UnknownNameThrowsArgumentOutOfRange()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => TokenizerFactory.Create("bogus"));
        Assert.Contains("bogus", ex.Message);
        Assert.Contains("huggingface", ex.Message);
    }

    [Fact]
    public void EveryNamedTokenizerHasMatchingName()
    {
        Assert.Equal("none", TokenizerFactory.Create("none").Name);
        Assert.Equal("char", TokenizerFactory.Create("char").Name);
        Assert.Equal("word", TokenizerFactory.Create("word").Name);
        Assert.Equal("regex", TokenizerFactory.Create("regex").Name);
    }
}

/// <summary>Final token recount of an assembled chunk (prefix + text).</summary>
public sealed class ChunkTokenCountTests
{
    [Fact]
    public void WithoutPrefixCountsChunkText()
    {
        var tokenizer = new WordTokenizer();
        Assert.Equal(3, ChunkTokenCount.ForFinalChunk(tokenizer, "one two three"));
        Assert.Equal(3, ChunkTokenCount.ForFinalChunk(tokenizer, "one two three", ""));
    }

    [Fact]
    public void WithPrefixCountsCombinedText()
    {
        var tokenizer = new WordTokenizer();
        Assert.Equal(3, ChunkTokenCount.ForFinalChunk(tokenizer, "two three", "one "));
    }

    [Fact]
    public void RejectsNullArguments()
    {
        Assert.Throws<ArgumentNullException>(() => ChunkTokenCount.ForFinalChunk(null!, "x"));
        Assert.Throws<ArgumentNullException>(() => ChunkTokenCount.ForFinalChunk(new WordTokenizer(), null!));
    }
}
