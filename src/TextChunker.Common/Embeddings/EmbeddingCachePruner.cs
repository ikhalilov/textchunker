namespace TextChunker.Embeddings;

/// <summary>Result of disk embedding cache maintenance.</summary>
public sealed record CachePruneResult(
    string Root,
    int Scanned,
    int Deleted,
    int TempDeleted,
    long BytesBefore,
    long BytesAfter,
    long BytesFreed,
    bool DryRun)
{
    public int Failed { get; init; }
}

/// <summary>
/// Cache maintenance: deletes records older than the TTL and, if a limit is set, the oldest
/// records until the limit is reached. Without limits and TTL it does nothing.
/// </summary>
public static class EmbeddingCachePruner
{
    private static readonly TimeSpan StaleTempAge = TimeSpan.FromDays(1);

    public static CachePruneResult Prune(
        string root,
        long? maxBytes,
        TimeSpan? maxAge,
        bool dryRun = false,
        DateTimeOffset? now = null)
    {
        if (string.IsNullOrWhiteSpace(root))
        {
            throw new ArgumentException("Каталог кэша не задан.", nameof(root));
        }
        if (maxBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(maxBytes), maxBytes, "Лимит кэша не может быть отрицательным.");
        }
        if (maxAge is { } age && age < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(maxAge), maxAge, "TTL кэша не может быть отрицательным.");
        }

        DateTimeOffset reference = now ?? DateTimeOffset.UtcNow;
        var files = new List<FileInfo>();
        int failed = 0;

        if (Directory.Exists(root))
        {
            foreach (string path in SafeEnumerate(root, "*.bin"))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (info.Exists)
                    {
                        files.Add(info);
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failed++;
                }
            }
        }

        long bytesBefore = files.Sum(file => file.Length);
        var keep = new List<FileInfo>(files.Count);
        int deleted = 0;
        int tempDeleted = 0;

        foreach (FileInfo file in files)
        {
            bool expired = maxAge is { } ttl && reference - file.LastWriteTimeUtc > ttl;
            if (expired)
            {
                if (Delete(file, dryRun))
                {
                    deleted++;
                }
                else
                {
                    failed++;
                    keep.Add(file);
                }
                continue;
            }
            keep.Add(file);
        }

        long bytesAfter = keep.Sum(file => file.Length);
        if (maxBytes is { } limit && bytesAfter > limit)
        {
            List<FileInfo> ordered = keep.OrderBy(file => file.LastWriteTimeUtc).ToList();
            for (int i = 0; i < ordered.Count; i++)
            {
                if (bytesAfter <= limit || i == ordered.Count - 1)
                {
                    // Do not delete the last record: the cache must not become empty.
                    break;
                }

                FileInfo file = ordered[i];
                long size = file.Length;
                if (Delete(file, dryRun))
                {
                    deleted++;
                    bytesAfter -= size;
                }
                else
                {
                    failed++;
                }
            }
        }

        if (Directory.Exists(root))
        {
            foreach (string path in SafeEnumerate(root, "*.tmp"))
            {
                try
                {
                    var info = new FileInfo(path);
                    if (!info.Exists || reference - info.LastWriteTimeUtc <= StaleTempAge)
                    {
                        continue;
                    }

                    if (Delete(info, dryRun))
                    {
                        tempDeleted++;
                    }
                    else
                    {
                        failed++;
                    }
                }
                catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
                {
                    failed++;
                }
            }
        }

        return new CachePruneResult(
            root,
            files.Count,
            deleted,
            tempDeleted,
            bytesBefore,
            bytesAfter,
            bytesBefore - bytesAfter,
            dryRun)
        {
            Failed = failed
        };
    }

    private static IEnumerable<string> SafeEnumerate(string root, string pattern)
    {
        try
        {
            return Directory.EnumerateFiles(root, pattern, SearchOption.AllDirectories).ToArray();
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return Array.Empty<string>();
        }
    }

    private static bool Delete(FileInfo file, bool dryRun)
    {
        if (dryRun)
        {
            return true;
        }

        try
        {
            file.Delete();
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}
