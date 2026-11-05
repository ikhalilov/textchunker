using TextChunker.Abstractions.Embeddings;
using TextChunker.Embeddings;

namespace TextChunker.Tests;

/// <summary>Обслуживание дискового кэша: TTL, лимит размера, dry-run, старые .tmp.</summary>
public sealed class EmbeddingCachePrunerTests : IDisposable
{
    private readonly string _root = Path.Combine(
        Path.GetTempPath(), $"textchunker-prune-{Guid.NewGuid():N}");

    private static readonly DateTimeOffset Now =
        new(2026, 11, 4, 12, 0, 0, TimeSpan.Zero);

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string WriteFile(string name, int size, TimeSpan age)
    {
        string path = Path.Combine(_root, "scope", name);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, new byte[size]);
        File.SetLastWriteTimeUtc(path, Now.Subtract(age).UtcDateTime);
        return path;
    }

    [Fact]
    public void DeletesEntriesOlderThanTtl()
    {
        string old = WriteFile("old.bin", 10, TimeSpan.FromDays(10));
        string fresh = WriteFile("fresh.bin", 20, TimeSpan.FromDays(1));

        CachePruneResult result = EmbeddingCachePruner.Prune(
            _root,
            maxBytes: null,
            maxAge: TimeSpan.FromDays(7),
            now: Now);

        Assert.Equal(2, result.Scanned);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(30, result.BytesBefore);
        Assert.Equal(20, result.BytesAfter);
        Assert.Equal(10, result.BytesFreed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void EnforcesMaxBytesDeletingOldestFirst()
    {
        string oldest = WriteFile("a.bin", 10, TimeSpan.FromDays(10));
        string middle = WriteFile("b.bin", 20, TimeSpan.FromDays(5));
        string newest = WriteFile("c.bin", 30, TimeSpan.FromDays(1));

        CachePruneResult result = EmbeddingCachePruner.Prune(
            _root,
            maxBytes: 25,
            maxAge: null,
            now: Now);

        Assert.Equal(2, result.Deleted);
        Assert.Equal(30, result.BytesAfter);
        Assert.False(File.Exists(oldest));
        Assert.False(File.Exists(middle));
        Assert.True(File.Exists(newest));
    }

    [Fact]
    public void DryRunReportsWithoutDeleting()
    {
        string old = WriteFile("old.bin", 10, TimeSpan.FromDays(10));
        string fresh = WriteFile("fresh.bin", 20, TimeSpan.FromDays(1));

        CachePruneResult result = EmbeddingCachePruner.Prune(
            _root,
            maxBytes: null,
            maxAge: TimeSpan.FromDays(7),
            dryRun: true,
            now: Now);

        Assert.True(result.DryRun);
        Assert.Equal(1, result.Deleted);
        Assert.Equal(20, result.BytesAfter);
        Assert.True(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void RemovesOnlyStaleTempFiles()
    {
        string oldTemp = WriteFile("old.bin.tmp", 5, TimeSpan.FromDays(2));
        string freshTemp = WriteFile("fresh.bin.tmp", 5, TimeSpan.FromHours(1));

        CachePruneResult result = EmbeddingCachePruner.Prune(
            _root,
            maxBytes: null,
            maxAge: null,
            now: Now);

        Assert.Equal(0, result.Scanned);
        Assert.Equal(1, result.TempDeleted);
        Assert.False(File.Exists(oldTemp));
        Assert.True(File.Exists(freshTemp));
    }

    [Fact]
    public void MissingRootReturnsEmptyResult()
    {
        CachePruneResult result = EmbeddingCachePruner.Prune(
            Path.Combine(_root, "missing"),
            maxBytes: null,
            maxAge: null,
            now: Now);

        Assert.Equal(0, result.Scanned);
        Assert.Equal(0, result.Deleted);
        Assert.Equal(0, result.BytesBefore);
    }

    [Fact]
    public void NegativeLimitsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmbeddingCachePruner.Prune(_root, maxBytes: -1, maxAge: null));
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            EmbeddingCachePruner.Prune(_root, maxBytes: null, maxAge: TimeSpan.FromSeconds(-1)));
    }

    [Fact]
    public void FactoryPrunesCacheWhenTtlConfigured()
    {
        string old = WriteFile("old.bin", 10, TimeSpan.FromDays(10));
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));

        EmbeddingProviderFactory.Create(new EmbeddingOptions
        {
            Provider = "hash",
            CacheDir = _root,
            CacheTtlDays = 1,
            UseCache = true
        });

        Assert.False(File.Exists(old));
    }
}
