using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TextChunker.IntegrationTests;

/// <summary>
/// CLI tests for semantic chunking. They use the offline <c>hash</c> provider,
/// so neither the network nor llama.cpp is needed; they verify limits, metadata and error codes.
/// </summary>
public sealed class SemanticCliTests
{
    private static string CliDll => Path.Combine(AppContext.BaseDirectory, "TextChunker.dll");

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr);

    private static CliResult Run(params string[] args)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            WorkingDirectory = AppContext.BaseDirectory
        };
        psi.ArgumentList.Add(CliDll);
        foreach (string arg in args)
        {
            psi.ArgumentList.Add(arg);
        }

        using var process = Process.Start(psi)!;
        Task<string> stdout = process.StandardOutput.ReadToEndAsync();
        Task<string> stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(120_000))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException("CLI не завершился за 120 секунд.");
        }
        Task.WaitAll(stdout, stderr);
        return new CliResult(process.ExitCode, stdout.Result, stderr.Result);
    }

    private static string TempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"textchunker-semantic-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static JsonElement[] ParseJsonLines(string stdout)
        => [.. stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(line => JsonDocument.Parse(line).RootElement)];

    [Fact]
    public void SemanticWithHashProviderProducesChunks()
    {
        string path = TempFile(
            "# Заголовок\n\nУстановка сервера. Запустите llama-server.\n\nФондовый рынок упал. Инвесторы продают.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--no-embedding-cache",
                "--tokenizer", "word",
                "--max-tokens", "20");

            Assert.Equal(0, result.ExitCode);
            JsonElement[] chunks = ParseJsonLines(result.StdOut);
            Assert.NotEmpty(chunks);

            foreach (JsonElement chunk in chunks)
            {
                Assert.True(chunk.GetProperty("tokenCount").GetInt32() <= 20);
                Assert.False(string.IsNullOrWhiteSpace(chunk.GetProperty("text").GetString()));

                JsonElement metadata = chunk.GetProperty("metadata");
                Assert.Equal("semantic", metadata.GetProperty("strategy").GetString());
                Assert.Equal("hash", metadata.GetProperty("semanticProvider").GetString());
                Assert.Equal(0.9, metadata.GetProperty("breakpointPercentile").GetDouble());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SemanticRespectsPrefixInTokenLimit()
    {
        string path = TempFile("One two three four five six seven eight nine ten.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--no-embedding-cache",
                "--tokenizer", "word",
                "--max-tokens", "6",
                "--prefix", "none ");

            Assert.Equal(0, result.ExitCode);
            JsonElement[] chunks = ParseJsonLines(result.StdOut);
            Assert.NotEmpty(chunks);
            foreach (JsonElement chunk in chunks)
            {
                string text = chunk.GetProperty("text").GetString()!;
                int words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
                Assert.True(words + 1 <= 6, $"'{text}' + prefix превышает 6 токенов");
                Assert.Equal(1, chunk.GetProperty("metadata").GetProperty("prefixTokens").GetInt32());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SemanticOversizedDocumentFallsBackToRecursive()
    {
        string path = TempFile(new string('a', 200));
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--no-embedding-cache",
                "--tokenizer", "char",
                "--max-chars", "50",
                "--semantic-max-bytes", "10",
                "--semantic-fallback", "recursive");

            Assert.Equal(0, result.ExitCode);
            JsonElement[] chunks = ParseJsonLines(result.StdOut);
            Assert.NotEmpty(chunks);
            Assert.Equal("recursive", chunks[0].GetProperty("metadata").GetProperty("semanticFallback").GetString());
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SemanticOversizedDocumentWithErrorExitsWith2()
    {
        string path = TempFile(new string('a', 200));
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--no-embedding-cache",
                "--tokenizer", "char",
                "--max-chars", "50",
                "--semantic-max-bytes", "10",
                "--semantic-fallback", "error");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("exceeds the semantic mode limit", result.StdErr, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(ParseJsonLines(result.StdOut));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnknownEmbeddingProviderExitsWith2()
    {
        string path = TempFile("One. Two.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "bogus");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("bogus", result.StdErr, StringComparison.Ordinal);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnreachableEmbeddingEndpointExitsWith5()
    {
        string path = TempFile("One. Two. Three.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "llama-cpp",
                "--embedding-endpoint", "http://127.0.0.1:1",
                "--no-embedding-cache",
                "--tokenizer", "word",
                "--max-tokens", "20");

            Assert.Equal(5, result.ExitCode);
            Assert.NotEmpty(result.StdErr);
            Assert.Empty(ParseJsonLines(result.StdOut));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void SemanticStatsIncludeAtomCountAndCacheCounters()
    {
        string path = TempFile(
            "# Заголовок\n\nУстановка сервера. Запустите llama-server.\n\nФондовый рынок упал. Инвесторы продают.");
        string cacheDir = Path.Combine(Path.GetTempPath(), $"textchunker-cache-{Guid.NewGuid():N}");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--embedding-cache-dir", cacheDir,
                "--tokenizer", "word",
                "--max-tokens", "20",
                "--stats");

            Assert.Equal(0, result.ExitCode);
            string statsLine = result.StdErr
                .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                .Last()
                .Trim();
            JsonElement stats = JsonDocument.Parse(statsLine).RootElement;

            Assert.True(stats.GetProperty("semanticAtomCount").GetInt32() > 0);
            Assert.True(stats.TryGetProperty("embeddingCacheHits", out _));
            Assert.True(stats.TryGetProperty("embeddingCacheMisses", out _));
            Assert.True(stats.GetProperty("embeddingCacheMisses").GetInt64() > 0);
        }
        finally
        {
            File.Delete(path);
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }

    [Fact]
    public void EmbeddingDimensionsIsConfigurable()
    {
        string path = TempFile("Установка сервера. Запустите llama-server. Рынок упал. Инвесторы продают.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--embedding-dimensions", "64",
                "--no-embedding-cache",
                "--tokenizer", "word",
                "--max-tokens", "20");

            Assert.Equal(0, result.ExitCode);
            Assert.NotEmpty(ParseJsonLines(result.StdOut));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EmbeddingMaxBatchCharsIsConfigurable()
    {
        string path = TempFile("Установка сервера. Запустите llama-server. Рынок упал. Инвесторы продают.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--embedding-max-batch-chars", "4000",
                "--no-embedding-cache",
                "--tokenizer", "word",
                "--max-tokens", "20");

            Assert.Equal(0, result.ExitCode);
            Assert.NotEmpty(ParseJsonLines(result.StdOut));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NegativeEmbeddingDimensionsExitWith2()
    {
        string path = TempFile("One. Two.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--embedding-dimensions", "-1");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("Embedding dimensions cannot be negative", result.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void PruneCacheDeletesExpiredEntries()
    {
        string root = Path.Combine(Path.GetTempPath(), $"textchunker-cli-prune-{Guid.NewGuid():N}");
        try
        {
            string scope = Path.Combine(root, "scope");
            Directory.CreateDirectory(scope);
            string old = Path.Combine(scope, "old.bin");
            string fresh = Path.Combine(scope, "fresh.bin");
            File.WriteAllBytes(old, new byte[10]);
            File.WriteAllBytes(fresh, new byte[20]);
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-10));

            var dry = Run(
                "--prune-cache",
                "--prune-cache-dry-run",
                "--embedding-cache-dir", root,
                "--embedding-cache-ttl-days", "7");

            Assert.Equal(0, dry.ExitCode);
            JsonElement stats = JsonDocument.Parse(dry.StdOut.Trim()).RootElement;
            Assert.Equal(2, stats.GetProperty("scanned").GetInt32());
            Assert.Equal(1, stats.GetProperty("deleted").GetInt32());
            Assert.True(stats.GetProperty("dryRun").GetBoolean());
            Assert.True(File.Exists(old));

            var real = Run(
                "--prune-cache",
                "--embedding-cache-dir", root,
                "--embedding-cache-ttl-days", "7");

            Assert.Equal(0, real.ExitCode);
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void NegativeCacheLimitExitsWith2()
    {
        var result = Run("--prune-cache", "--embedding-cache-max-bytes", "-1");

        Assert.Equal(2, result.ExitCode);
        Assert.Contains("cache-max-bytes", result.StdErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void StreamingFallbackChunksLargeDocument()
    {
        string[] lines = Enumerable.Range(0, 20)
            .Select(i => $"Строка {i}: длинное предложение для проверки потоковых окон.")
            .ToArray();
        string path = TempFile(string.Join("\n", lines));
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--no-embedding-cache",
                "--semantic-fallback", "streaming",
                "--semantic-max-bytes", "10",
                "--semantic-stream-window-bytes", "200",
                "--semantic-stream-overlap-bytes", "50",
                "--tokenizer", "word",
                "--max-tokens", "20");

            Assert.Equal(0, result.ExitCode);
            JsonElement[] chunks = ParseJsonLines(result.StdOut);
            Assert.NotEmpty(chunks);
            for (int i = 0; i < chunks.Length; i++)
            {
                Assert.Equal(i + 1, chunks[i].GetProperty("seq").GetInt32());
                Assert.Equal("streaming", chunks[i].GetProperty("metadata").GetProperty("semanticFallback").GetString());
            }
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StreamingOverlapNotLessThanWindowExitsWith2()
    {
        string path = TempFile("One. Two.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--semantic-fallback", "streaming",
                "--semantic-stream-window-bytes", "100",
                "--semantic-stream-overlap-bytes", "100");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("overlap", result.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidEmbeddingConcurrencyExitsWith2()
    {
        string path = TempFile("One. Two.");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "semantic",
                "--embedding-provider", "hash",
                "--embedding-concurrency", "0");

            Assert.Equal(2, result.ExitCode);
            Assert.Contains("concurrency", result.StdErr, StringComparison.OrdinalIgnoreCase);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void NonSemanticStrategyIgnoresEmbeddingOptions()
    {
        string path = TempFile("abcdefghij");
        try
        {
            var result = Run(
                "--input", path,
                "--strategy", "fixed",
                "--embedding-provider", "bogus",
                "--tokenizer", "char",
                "--max-chars", "5");

            // A non-semantic strategy must not fail because of unused options.
            Assert.Equal(0, result.ExitCode);
            Assert.Equal(2, ParseJsonLines(result.StdOut).Length);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
