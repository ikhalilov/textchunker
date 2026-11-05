using TextChunker.Abstractions;
using TextChunker.Chunking;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

public sealed class SeparatorOverlapTests
{
    [Fact]
    public void RecursiveAppliesCharOverlapBetweenChunks()
    {
        const string text = "aaaa. bbbb. cccc. dddd. eeee. ffff. gggg.";
        var options = new ChunkingOptions(maxChars: 20, overlapChars: 5, trim: false);
        ChunkSplit split = new RecursiveChunker().Chunk(text, new WordTokenizer(), options, final: true);

        Assert.True(split.Segments.Count >= 2);
        Assert.Equal([0, 13, 25], [.. split.Segments.Select(s => s.StartInText)]);
        Assert.Equal([18, 30, 41], [.. split.Segments.Select(s => s.EndInText)]);
        Assert.Equal(41, split.ConsumedEnd);

        for (int i = 1; i < split.Segments.Count; i++)
        {
            ChunkSegment previous = split.Segments[i - 1];
            ChunkSegment current = split.Segments[i];
            Assert.Equal(5, previous.EndInText - current.StartInText);
        }
    }

    [Fact]
    public void ParagraphAppliesCharOverlapBetweenChunks()
    {
        const string text = "p1\n\np2\n\np3\n\np4\n\np5";
        var options = new ChunkingOptions(strategy: "paragraph", maxChars: 10, overlapChars: 2, trim: false);
        ChunkSplit split = new ParagraphChunker().Chunk(text, new WordTokenizer(), options, final: true);

        Assert.Equal([0, 6, 14], [.. split.Segments.Select(s => s.StartInText)]);
        Assert.Equal([8, 16, 18], [.. split.Segments.Select(s => s.EndInText)]);
        Assert.Equal(18, split.ConsumedEnd);

        for (int i = 1; i < split.Segments.Count; i++)
        {
            Assert.Equal(2, split.Segments[i - 1].EndInText - split.Segments[i].StartInText);
        }
    }

    [Fact]
    public void RecursiveAppliesTokenOverlapBetweenChunks()
    {
        const string text = "one two three four five six seven eight";
        var tokenizer = new WordTokenizer();
        var options = new ChunkingOptions(maxTokens: 4, overlapTokens: 1, trim: false);
        ChunkSplit split = new RecursiveChunker().Chunk(text, tokenizer, options, final: true);

        Assert.True(split.Segments.Count >= 2);
        for (int i = 1; i < split.Segments.Count; i++)
        {
            ChunkSegment previous = split.Segments[i - 1];
            ChunkSegment current = split.Segments[i];
            Assert.True(current.StartInText < previous.EndInText, "соседние чанки должны перекрываться");
            string overlapText = text.Substring(current.StartInText, previous.EndInText - current.StartInText);
            Assert.Equal(1, tokenizer.CountTokens(overlapText));
            Assert.True(current.StartInText > previous.StartInText, "перекрытие не должно ломать прогресс");
        }
    }
}
