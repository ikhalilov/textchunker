using TextChunker.Chunking;

namespace TextChunker.Tests;

public sealed class ChunkerFactoryTests
{
    [Fact]
    public void DefaultsToRecursive()
    {
        Assert.IsType<RecursiveChunker>(ChunkerFactory.Create(null));
        Assert.IsType<RecursiveChunker>(ChunkerFactory.Create("  "));
    }

    [Theory]
    [InlineData("fixed", typeof(FixedChunker))]
    [InlineData("recursive", typeof(RecursiveChunker))]
    [InlineData("paragraph", typeof(ParagraphChunker))]
    [InlineData("FIXED", typeof(FixedChunker))]
    public void MapsNames(string name, Type expected)
    {
        Assert.IsType(expected, ChunkerFactory.Create(name));
    }

    [Fact]
    public void UnknownStrategyThrows()
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ChunkerFactory.Create("bogus"));
        Assert.Contains("bogus", ex.Message);
        Assert.Contains("recursive", ex.Message);
    }
}
