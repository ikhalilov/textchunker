using TextChunker.Abstractions;
using TextChunker.Chunking;

namespace TextChunker.Tests;

public sealed class ChunkingOptionsValidatorTests
{
    [Fact]
    public void RejectsNonPositiveMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChunkingOptionsValidator.Validate(new ChunkingOptions(maxChars: 0)));
    }

    [Fact]
    public void RejectsMinAboveMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChunkingOptionsValidator.Validate(new ChunkingOptions(maxChars: 10, minChars: 11)));
    }

    [Fact]
    public void RejectsOverlapNotBelowMax()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChunkingOptionsValidator.Validate(new ChunkingOptions(maxChars: 10, overlapChars: 10)));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => ChunkingOptionsValidator.Validate(new ChunkingOptions(maxTokens: 10, overlapTokens: 12)));
    }

    [Fact]
    public void AcceptsValidOptions()
    {
        var options = new ChunkingOptions(maxChars: 10, minChars: 2, overlapChars: 3);
        ChunkingOptionsValidator.Validate(options);
    }
}
