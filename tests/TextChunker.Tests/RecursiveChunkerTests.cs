using TextChunker.Abstractions;
using TextChunker.Chunking;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

public sealed class RecursiveChunkerTests
{
    private static ChunkSplit Run(string text, ChunkingOptions options, bool final = true)
        => new RecursiveChunker().Chunk(text, new WordTokenizer(), options, final);

    [Fact]
    public void SplitsOnSentenceBoundaries()
    {
        var split = Run("alpha. beta. gamma.", new ChunkingOptions(maxChars: 10));
        Assert.Equal([ "alpha.", "beta.", "gamma." ], [.. split.Segments.Select(s => s.Text)]);
    }

    [Fact]
    public void HardSplitsOversizedPiece()
    {
        var split = Run("abcdefghijklmnop", new ChunkingOptions(maxChars: 5));
        Assert.All(split.Segments, s => Assert.True(s.Text.Length <= 5));
        Assert.Equal("abcdefghijklmnop", string.Concat(split.Segments.Select(s => s.Text)));
    }

    [Fact]
    public void HoldsUnterminatedTail()
    {
        var options = new ChunkingOptions(maxChars: 100);
        var chunker = new RecursiveChunker();
        ChunkSplit split = chunker.Chunk("hello wor", new WordTokenizer(), options, final: false);
        Assert.Empty(split.Segments);
        Assert.True(split.MoreNeeded);
    }

    [Fact]
    public void EmitsCompleteTailAtEnd()
    {
        var split = Run("hello world", new ChunkingOptions(maxChars: 100));
        Assert.Equal([ "hello world" ], [.. split.Segments.Select(s => s.Text)]);
        Assert.False(split.MoreNeeded);
    }

    [Fact]
    public void RespectsTokenLimitWithPrefix()
    {
        var tokenizer = new WordTokenizer();
        var options = new ChunkingOptions(maxTokens: 6, prefix: "title: x | text: ");
        var split = Run("one two three four five six seven eight", options);
        Assert.NotEmpty(split.Segments);
        foreach (ChunkSegment segment in split.Segments)
        {
            Assert.True(tokenizer.CountTokens(options.Prefix + segment.Text) <= 6);
        }
    }

    [Fact]
    public void RussianAbbreviationsPreserveTextAndLimit()
    {
        // Russian abbreviations (т. д., т. п., см., стр.): we verify that chunking
        // does not lose text and does not exceed the limit (boundaries may fall inside
        // an abbreviation under a hard fallback — that is acceptable).
        const string text = "Это т. д. пример. Далее и т. п. текст: см. стр. 10. Конец.";
        var options = new ChunkingOptions(maxChars: 12, trim: false);
        var chunker = new RecursiveChunker();

        ChunkSplit split = chunker.Chunk(text, new WordTokenizer(), options, final: true);
        Assert.Equal(text, string.Concat(split.Segments.Select(s => s.Text)));
        Assert.All(split.Segments, s => Assert.NotEmpty(s.Text));
        Assert.All(split.Segments, s => Assert.True(s.Text.Length <= 12));

        ChunkSplit second = chunker.Chunk(text, new WordTokenizer(), options, final: true);
        Assert.Equal(
            split.Segments.Select(s => (s.StartInText, s.EndInText)),
            second.Segments.Select(s => (s.StartInText, s.EndInText)));
    }
}
