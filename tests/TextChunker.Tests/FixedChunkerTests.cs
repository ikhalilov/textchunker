using TextChunker.Abstractions;
using TextChunker.Chunking;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

public sealed class FixedChunkerTests
{
    private static ChunkSplit Run(string text, ChunkingOptions options, bool final = true)
        => new FixedChunker().Chunk(text, new WordTokenizer(), options, final);

    [Fact]
    public void SplitsByChars()
    {
        var split = Run("abcdefghij", new ChunkingOptions(strategy: "fixed", maxChars: 4));
        Assert.Equal([ "abcd", "efgh", "ij" ], [.. split.Segments.Select(s => s.Text)]);
        Assert.False(split.MoreNeeded);
        Assert.Equal(10, split.ConsumedEnd);
    }

    [Fact]
    public void AppliesCharOverlap()
    {
        var split = Run("abcdefghij", new ChunkingOptions(strategy: "fixed", maxChars: 4, overlapChars: 2));
        Assert.Equal([ "abcd", "cdef", "efgh", "ghij" ], [.. split.Segments.Select(s => s.Text)]);
    }

    [Fact]
    public void SplitsByWords()
    {
        var split = Run("one two three four", new ChunkingOptions(strategy: "fixed", maxWords: 2));
        Assert.Equal([ "one two", "three four" ], [.. split.Segments.Select(s => s.Text)]);
    }

    [Fact]
    public void SplitsByTokensWithWordTokenizer()
    {
        var split = Run("aa bb cc dd", new ChunkingOptions(strategy: "fixed", maxTokens: 2));
        Assert.Equal([ "aa bb", "cc dd" ], [.. split.Segments.Select(s => s.Text)]);
    }

    [Fact]
    public void PrefixCountsTowardTokenLimit()
    {
        var options = new ChunkingOptions(strategy: "fixed", maxTokens: 3, prefix: "P ");
        var split = Run("aa bb cc dd", options);
        Assert.Equal([ "aa bb", "cc dd" ], [.. split.Segments.Select(s => s.Text)]);
        foreach (ChunkSegment segment in split.Segments)
        {
            Assert.True(new WordTokenizer().CountTokens("P " + segment.Text) <= 3);
        }
    }

    [Fact]
    public void DoesNotSplitSurrogatePairsByChars()
    {
        // 'a' + U+1F600 (2 UTF-16 units) + 'b'
        const string text = "a\U0001F600b";
        var split = Run(text, new ChunkingOptions(strategy: "fixed", maxChars: 2));
        Assert.Equal("ab", string.Concat(split.Segments.Select(s => s.Text)).Replace("\U0001F600", ""));
        foreach (ChunkSegment segment in split.Segments)
        {
            Assert.False(char.IsHighSurrogate(segment.Text[^1]) || char.IsLowSurrogate(segment.Text[0]));
        }
    }

    [Fact]
    public void HoldsTailUntilMoreTextArrives()
    {
        var options = new ChunkingOptions(strategy: "fixed", maxChars: 4);
        var chunker = new FixedChunker();

        // Exactly 4 characters: the text may continue, so we do not emit anything.
        ChunkSplit first = chunker.Chunk("abcd", new WordTokenizer(), options, final: false);
        Assert.Empty(first.Segments);
        Assert.True(first.MoreNeeded);
        Assert.Equal(0, first.ConsumedEnd);

        // 5 characters: the first chunk is emitted, and the tail (1 character) is held back.
        ChunkSplit second = chunker.Chunk("abcde", new WordTokenizer(), options, final: false);
        Assert.Equal(new[] { "abcd" }, second.Segments.Select(s => s.Text).ToArray());
        Assert.True(second.MoreNeeded);
        Assert.Equal(4, second.ConsumedEnd);
    }

    [Fact]
    public void EmptyTextProducesNothing()
    {
        ChunkSplit split = Run("", new ChunkingOptions(strategy: "fixed", maxChars: 4));
        Assert.Empty(split.Segments);
        Assert.Equal(0, split.ConsumedEnd);
    }
}
