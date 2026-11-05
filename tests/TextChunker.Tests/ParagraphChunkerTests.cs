using TextChunker.Abstractions;
using TextChunker.Chunking;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

public sealed class ParagraphChunkerTests
{
    private static ChunkSplit Run(string text, ChunkingOptions options, bool final = true)
        => new ParagraphChunker().Chunk(text, new WordTokenizer(), options, final);

    [Fact]
    public void MergesSmallParagraphs()
    {
        var split = Run("p1\n\np2\n\np3", new ChunkingOptions(strategy: "paragraph", maxChars: 10));
        Assert.Equal([ "p1\n\np2\n\np3" ], [.. split.Segments.Select(s => s.Text)]);
    }

    [Fact]
    public void SplitsWhenParagraphsDoNotFit()
    {
        var split = Run("p1\n\np2\n\np3", new ChunkingOptions(strategy: "paragraph", maxChars: 8));
        Assert.Equal([ "p1\n\np2", "p3" ], [.. split.Segments.Select(s => s.Text)]);
    }

    [Fact]
    public void SubSplitsOversizedParagraph()
    {
        string text = new('x', 12);
        var split = Run(text, new ChunkingOptions(strategy: "paragraph", maxChars: 5));
        Assert.All(split.Segments, s => Assert.True(s.Text.Length <= 5));
        Assert.Equal(text, string.Concat(split.Segments.Select(s => s.Text)));
    }
}
