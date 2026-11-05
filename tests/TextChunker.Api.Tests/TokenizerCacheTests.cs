using TextChunker.Api.Services;

namespace TextChunker.Api.Tests;

public sealed class TokenizerCacheTests
{
    [Fact]
    public void ReusesSuccessfullyLoadedTokenizers()
    {
        var cache = new TokenizerCache();

        var first = cache.Get("char", null, null);
        var second = cache.Get("char", null, null);
        var other = cache.Get("word", null, null);

        Assert.Same(first, second);
        Assert.NotSame(first, other);
    }
}
