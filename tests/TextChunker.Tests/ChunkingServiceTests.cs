using System.Text;

using TextChunker.Abstractions;
using TextChunker.Chunking;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

public sealed class ChunkingServiceTests
{
    private static ChunkingStats Run(
        string[] lines,
        ChunkingOptions options,
        CollectingWriter writer,
        ITokenizer? tokenizer = null,
        Dictionary<string, object?>? userMetadata = null)
    {
        var service = new ChunkingService(
            tokenizer ?? new WordTokenizer(),
            ChunkerFactory.Create(options.Strategy),
            options,
            normalized: false,
            userMetadata);
        return service.Process(new FakeSource(lines), writer);
    }

    [Fact]
    public void BuildsChunksWithOffsetsAndCounts()
    {
        var writer = new CollectingWriter();
        Run(["abcdefghij"], new ChunkingOptions(strategy: "fixed", maxChars: 4), writer);

        Assert.Equal(["abcd", "efgh", "ij"], writer.Texts);
        Assert.Equal([0, 4, 8], writer.Chunks.Select(c => c.StartChar).ToArray());
        Assert.Equal([4, 8, 10], writer.Chunks.Select(c => c.EndChar).ToArray());
        Assert.Equal([1, 2, 3], [.. writer.Chunks.Select(c => c.Seq)]);
        Assert.Equal(["test:000001", "test:000002", "test:000003"], [.. writer.Chunks.Select(c => c.Id)]);
        Assert.All(writer.Chunks, c => Assert.Equal(c.Text.Length, c.CharCount));
        Assert.All(writer.Chunks, c => Assert.Equal(Encoding.UTF8.GetByteCount(c.Text), c.ByteCount));
        Assert.All(writer.Chunks, c => Assert.Equal(1, c.TokenCount));
    }

    [Fact]
    public void MultiLineOffsetsFollowJoinedText()
    {
        var writer = new CollectingWriter();
        Run(["aa", "bb"], new ChunkingOptions(strategy: "fixed", maxChars: 2), writer);

        Assert.Equal(["aa", "bb"], writer.Texts);
        Assert.Equal([0, 3], writer.Chunks.Select(c => c.StartChar).ToArray());
        Assert.Equal([2, 5], writer.Chunks.Select(c => c.EndChar).ToArray());
    }

    [Fact]
    public void TokenCountIncludesPrefix()
    {
        var writer = new CollectingWriter();
        var options = new ChunkingOptions(strategy: "fixed", maxTokens: 3, prefix: "P ", trim: true);
        Run(["aa bb cc dd"], options, writer);

        Assert.Equal(["aa bb", "cc dd"], writer.Texts);
        Assert.All(writer.Chunks, c => Assert.Equal(3, c.TokenCount));
        Assert.Equal(1, writer.Chunks[0].Metadata["prefixTokens"]);
    }

    [Fact]
    public void MetadataContainsStrategyTokenizerAndUnit()
    {
        var writer = new CollectingWriter();
        Run(["abc"], new ChunkingOptions(strategy: "fixed", maxChars: 10), writer);

        var metadata = writer.Chunks[0].Metadata;
        Assert.Equal("fixed", metadata["strategy"]);
        Assert.Equal("word", metadata["tokenizer"]);
        Assert.Equal("chars", metadata["unit"]);
        Assert.Equal(false, metadata["normalized"]);
        Assert.Equal(10, metadata["maxSize"]);
    }

    [Fact]
    public void UserMetadataIsPreserved()
    {
        var writer = new CollectingWriter();
        var user = new Dictionary<string, object?> { ["lang"] = "ru", ["project"] = "kb" };
        Run(["abc"], new ChunkingOptions(strategy: "fixed", maxChars: 10), writer, userMetadata: user);

        Assert.Equal("ru", writer.Chunks[0].Metadata["lang"]);
        Assert.Equal("kb", writer.Chunks[0].Metadata["project"]);
    }

    [Fact]
    public void UserMetadataSourceOverridesChunkSourceAndId()
    {
        var writer = new CollectingWriter();
        var user = new Dictionary<string, object?> { ["source"] = "doc.md" };
        Run(["abc"], new ChunkingOptions(strategy: "fixed", maxChars: 10), writer, userMetadata: user);

        Assert.Equal("doc.md", writer.Chunks[0].Source);
        Assert.Equal("doc.md:000001", writer.Chunks[0].Id);
        Assert.Equal("doc.md", writer.Chunks[0].Metadata["source"]);
    }

    [Fact]
    public void DropSmallDropsFinalUndersizedChunk()
    {
        var writer = new CollectingWriter();
        var stats = Run(
            ["abcdefghijk"],
            new ChunkingOptions(strategy: "fixed", maxChars: 10, minChars: 2, dropSmall: true),
            writer);

        Assert.Equal(["abcdefghij"], writer.Texts);
        Assert.Equal(1, stats.ChunkCount);
    }

    [Fact]
    public void WithoutDropSmallKeepsFinalUndersizedChunk()
    {
        var writer = new CollectingWriter();
        Run(
            ["abcdefghijk"],
            new ChunkingOptions(strategy: "fixed", maxChars: 10, minChars: 2, dropSmall: false),
            writer);

        Assert.Equal(["abcdefghij", "k"], writer.Texts);
    }

    [Fact]
    public void IsDeterministicAcrossRuns()
    {
        var options = new ChunkingOptions(strategy: "recursive", maxChars: 20);
        var first = new CollectingWriter();
        var second = new CollectingWriter();

        Run(["one. two. three. four.", "five six seven"], options, first);
        Run(["one. two. three. four.", "five six seven"], options, second);

        Assert.Equal(first.Texts, second.Texts);
        Assert.Equal(
            first.Chunks.Select(c => (c.Id, c.Seq, c.StartChar, c.EndChar)),
            second.Chunks.Select(c => (c.Id, c.Seq, c.StartChar, c.EndChar)));
    }

    [Fact]
    public void StatsAreAggregated()
    {
        var writer = new CollectingWriter();
        var stats = Run(["aa bb cc dd"], new ChunkingOptions(strategy: "fixed", maxTokens: 2), writer);

        Assert.Equal(2, stats.ChunkCount);
        Assert.Equal(2, stats.MinChunkTokens);
        Assert.Equal(2, stats.MaxChunkTokens);
        Assert.Equal(2, stats.AvgChunkTokens);
        Assert.Equal(11, stats.InputChars);
        Assert.Equal(11, stats.InputBytes);
    }

    [Fact]
    public void EmptySourceProducesNoChunks()
    {
        var writer = new CollectingWriter();
        var stats = Run([], new ChunkingOptions(strategy: "fixed", maxChars: 4), writer);

        Assert.Empty(writer.Chunks);
        Assert.Equal(0, stats.ChunkCount);
    }

    [Fact]
    public void EmptyLinesArePreservedInOffsets()
    {
        var writer = new CollectingWriter();
        Run(
            ["ab", "", "cd"],
            new ChunkingOptions(strategy: "fixed", maxChars: 100, trim: false),
            writer);

        var item = Assert.Single(writer.Chunks);
        Assert.Equal("ab\n\ncd", item.Text);
        Assert.Equal(0L, writer.Chunks[0].StartChar);
        Assert.Equal(6L, writer.Chunks[0].EndChar);
    }
}
