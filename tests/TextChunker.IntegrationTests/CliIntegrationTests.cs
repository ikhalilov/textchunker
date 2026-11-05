using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace TextChunker.IntegrationTests;

/// <summary>
/// Интеграционные тесты CLI: коды завершения, валидный JSONL в stdout, ошибки в stderr,
/// stdin, --stats, --format text, отсутствие BOM в файле вывода.
/// Запускают реальный процесс <c>dotnet TextChunker.dll</c> из каталога тестовой сборки.
/// </summary>
public sealed class CliIntegrationTests
{
    private static string CliDll => Path.Combine(AppContext.BaseDirectory, "TextChunker.dll");

    private sealed record CliResult(int ExitCode, string StdOut, string StdErr, TimeSpan Elapsed);

    private static CliResult Run(
        string[] args,
        string? stdin = null,
        int timeoutMs = 120_000)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = stdin is not null,
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

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Не удалось запустить dotnet TextChunker.dll");

        if (stdin is not null)
        {
            process.StandardInput.Write(stdin);
            process.StandardInput.Close();
        }

        var stopwatch = Stopwatch.StartNew();
        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit(timeoutMs))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"CLI не завершился за {timeoutMs / 1000} секунд.");
        }
        stopwatch.Stop();
        Task.WaitAll(stdoutTask, stderrTask);

        return new CliResult(process.ExitCode, stdoutTask.Result, stderrTask.Result, stopwatch.Elapsed);
    }

    private static string TempFile(string content)
    {
        string path = Path.Combine(Path.GetTempPath(), $"textchunker-cli-{Guid.NewGuid():N}.txt");
        File.WriteAllText(path, content, new UTF8Encoding(false));
        return path;
    }

    private static JsonElement[] ParseJsonLines(string stdout)
    {
        return stdout
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToArray();
    }

    [Fact]
    public void FileInputProducesValidJsonLines()
    {
        string path = TempFile("Привет, мир! Это тестовый документ для нарезки.\nВторой абзац текста.");
        try
        {
            var result = Run(new[]
            {
                "--input", path,
                "--strategy", "fixed",
                "--max-chars", "20",
                "--tokenizer", "char"
            });

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StdErr.Trim());

            JsonElement[] chunks = ParseJsonLines(result.StdOut);
            Assert.NotEmpty(chunks);
            foreach (JsonElement chunk in chunks)
            {
                Assert.True(chunk.TryGetProperty("id", out _));
                Assert.True(chunk.TryGetProperty("seq", out _));
                Assert.True(chunk.TryGetProperty("source", out _));
                Assert.True(chunk.TryGetProperty("text", out _));
                Assert.True(chunk.TryGetProperty("charCount", out _));
                Assert.True(chunk.TryGetProperty("tokenCount", out _));
                Assert.True(chunk.TryGetProperty("metadata", out _));
                Assert.True(chunk.GetProperty("text").GetString()!.Length <= 20);
            }

            Assert.Contains(chunks, c => c.GetProperty("text").GetString()!.Contains("Привет"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MissingFileExitsWith3()
    {
        var result = Run(new[] { "--input", Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid()) });
        Assert.Equal(3, result.ExitCode);
        Assert.NotEmpty(result.StdErr);
        Assert.Empty(result.StdOut);
    }

    [Fact]
    public void UnknownStrategyExitsWith2()
    {
        string path = TempFile("abc");
        try
        {
            var result = Run(new[] { "--input", path, "--strategy", "bogus" });
            Assert.Equal(2, result.ExitCode);
            Assert.Contains("bogus", result.StdErr);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnknownTokenizerExitsWith2()
    {
        string path = TempFile("abc");
        try
        {
            var result = Run(new[] { "--input", path, "--tokenizer", "bogus" });
            Assert.Equal(2, result.ExitCode);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void InvalidUtf8StrictModeExitsWith4()
    {
        string path = Path.Combine(Path.GetTempPath(), $"textchunker-bad-utf8-{Guid.NewGuid():N}.txt");
        File.WriteAllBytes(path, new byte[] { 0x41, 0x80, 0x42 });
        try
        {
            var result = Run(new[] { "--input", path, "--invalid-encoding", "error", "--tokenizer", "char" });
            Assert.Equal(4, result.ExitCode);
            Assert.NotEmpty(result.StdErr);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void UnwritableOutputExitsWith6()
    {
        string input = TempFile("abc");
        string missingDir = Path.Combine(Path.GetTempPath(), $"textchunker-missing-{Guid.NewGuid():N}");
        string output = Path.Combine(missingDir, "out.jsonl");
        try
        {
            var result = Run(new[] { "--input", input, "--output", output, "--tokenizer", "char" });
            Assert.Equal(6, result.ExitCode);
            Assert.NotEmpty(result.StdErr);
        }
        finally
        {
            File.Delete(input);
            if (Directory.Exists(missingDir))
            {
                Directory.Delete(missingDir, recursive: true);
            }
        }
    }

    [Fact]
    public void BrokenTokenizerFileExitsWith5()
    {
        string input = TempFile("abc");
        string tokenizer = Path.Combine(Path.GetTempPath(), $"textchunker-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(tokenizer, "{ this is not json");
        try
        {
            var result = Run(new[]
            {
                "--input", input,
                "--tokenizer", "huggingface",
                "--tokenizer-model", tokenizer
            });

            Assert.Equal(5, result.ExitCode);
            Assert.NotEmpty(result.StdErr);
        }
        finally
        {
            File.Delete(input);
            File.Delete(tokenizer);
        }
    }

    [Fact]
    public void StdinIsReadWhenInputIsDash()
    {
        var result = Run(
            new[] { "--input", "-", "--strategy", "fixed", "--max-chars", "5", "--tokenizer", "char" },
            stdin: "abcdefghij");

        Assert.Equal(0, result.ExitCode);
        JsonElement[] chunks = ParseJsonLines(result.StdOut);
        Assert.Equal(2, chunks.Length);
        Assert.Equal("abcde", chunks[0].GetProperty("text").GetString());
        Assert.Equal("fghij", chunks[1].GetProperty("text").GetString());
    }

    [Fact]
    public void TextFormatUsesChunkMarkers()
    {
        var result = Run(
            new[] { "--input", "-", "--strategy", "fixed", "--max-chars", "5", "--tokenizer", "char", "--format", "text" },
            stdin: "abcdefghij");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("=== CHUNK 000001 ===", result.StdOut);
        Assert.Contains("=== CHUNK 000002 ===", result.StdOut);
        Assert.Contains("abcde", result.StdOut);
        Assert.Contains("fghij", result.StdOut);
    }

    [Fact]
    public void StatsGoToStderrWhileStdoutStaysValidJson()
    {
        var result = Run(
            new[] { "--input", "-", "--strategy", "fixed", "--max-chars", "5", "--tokenizer", "char", "--stats" },
            stdin: "abcdefghij");

        Assert.Equal(0, result.ExitCode);
        JsonElement[] chunks = ParseJsonLines(result.StdOut);
        Assert.Equal(2, chunks.Length);

        JsonElement stats = JsonDocument.Parse(result.StdErr.Trim()).RootElement;
        Assert.Equal(2, stats.GetProperty("chunkCount").GetInt32());
        Assert.True(stats.TryGetProperty("minChunkTokens", out _));
        Assert.True(stats.TryGetProperty("avgChunkTokens", out _));
        Assert.True(stats.TryGetProperty("maxChunkTokens", out _));
        Assert.True(stats.TryGetProperty("inputBytes", out _));
        Assert.True(stats.TryGetProperty("inputChars", out _));
        Assert.True(stats.TryGetProperty("elapsedMs", out _));
    }

    [Fact]
    public void OutputFileHasNoBomAndValidJson()
    {
        string input = TempFile("abcdefghij");
        string output = Path.Combine(Path.GetTempPath(), $"textchunker-out-{Guid.NewGuid():N}.jsonl");
        try
        {
            var result = Run(new[]
            {
                "--input", input,
                "--strategy", "fixed",
                "--max-chars", "5",
                "--tokenizer", "char",
                "--output", output
            });

            Assert.Equal(0, result.ExitCode);
            Assert.Empty(result.StdOut);

            byte[] bytes = File.ReadAllBytes(output);
            Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF);
            JsonElement[] chunks = ParseJsonLines(File.ReadAllText(output, Encoding.UTF8));
            Assert.Equal(2, chunks.Length);
        }
        finally
        {
            File.Delete(input);
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Fact]
    public void DefaultTokenizerWarnsOnStderr()
    {
        var result = Run(
            ["--input", "-", "--strategy", "fixed", "--max-chars", "100"],
            stdin: "hello world");

        Assert.Equal(0, result.ExitCode);
        Assert.Contains("warning", result.StdErr);
        Assert.Single(ParseJsonLines(result.StdOut));
    }

    [Fact]
    public void EmptyInputProducesNoChunksAndExitsZero()
    {
        var result = Run(
            ["--input", "-", "--strategy", "fixed", "--max-chars", "5", "--tokenizer", "char"],
            stdin: "");

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(ParseJsonLines(result.StdOut));
    }

    [Fact]
    public void VersionPrintsVersionAndExitsZero()
    {
        var result = Run(new[] { "--version" });

        Assert.Equal(0, result.ExitCode);
        Assert.Matches(@"^\d+\.\d+\.\d+", result.StdOut.Trim());
        Assert.Empty(result.StdErr.Trim());
    }

    [Fact]
    public void GoldenRecursiveCharFixtureMatchesExpectedJsonl()
    {
        string input = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-input.txt");
        string expectedPath = Path.Combine(AppContext.BaseDirectory, "Fixtures", "golden-expected.jsonl");

        var result = Run(new[]
        {
            "--input", input,
            "--strategy", "recursive",
            "--max-chars", "50",
            "--tokenizer", "char",
            "--metadata", "lang=ru",
            "--metadata", "project=kb"
        });

        Assert.Equal(0, result.ExitCode);
        string[] expected = File.ReadAllLines(expectedPath);
        string[] actual = result.StdOut.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(expected, actual);
    }

    private static string WriteRepeatedFile(long targetBytes, bool singleLine)
    {
        string path = Path.Combine(Path.GetTempPath(), $"textchunker-load-{Guid.NewGuid():N}.txt");
        string line = "Строка тестового корпуса: alpha beta gamma 12345.";
        using var writer = new StreamWriter(path, append: false, new UTF8Encoding(false));
        long written = 0;
        while (written < targetBytes)
        {
            if (singleLine)
            {
                writer.Write(line);
                writer.Write(' ');
                written += Encoding.UTF8.GetByteCount(line) + 1;
            }
            else
            {
                writer.WriteLine(line);
                written += Encoding.UTF8.GetByteCount(line) + 2;
            }
        }
        return path;
    }

    private static void AssertLoadTest(int megabytes, bool singleLine)
    {
        string input = WriteRepeatedFile(megabytes * 1024L * 1024L, singleLine);
        string output = Path.Combine(Path.GetTempPath(), $"textchunker-load-out-{Guid.NewGuid():N}.jsonl");
        try
        {
            var result = Run(new[]
            {
                "--input", input,
                "--output", output,
                "--strategy", "fixed",
                "--max-chars", "1000",
                "--tokenizer", "char",
                "--stats"
            }, timeoutMs: 900_000);

            Assert.Equal(0, result.ExitCode);

            int count = 0;
            foreach (string line in File.ReadLines(output))
            {
                using var document = JsonDocument.Parse(line);
                JsonElement chunk = document.RootElement;
                Assert.True(chunk.GetProperty("text").GetString()!.Length <= 1000);
                Assert.True(chunk.GetProperty("tokenCount").GetInt32() <= 1000);
                count++;
            }

            Assert.True(count > 0);
            JsonElement stats = JsonDocument.Parse(result.StdErr.Trim()).RootElement;
            Assert.Equal(count, stats.GetProperty("chunkCount").GetInt32());

            Console.WriteLine(
                $"[load] {megabytes} MB singleLine={singleLine}: chunks={count}, " +
                $"elapsed={result.Elapsed.TotalSeconds:F1}s");
        }
        finally
        {
            File.Delete(input);
            if (File.Exists(output))
            {
                File.Delete(output);
            }
        }
    }

    [Fact]
    public void TenMegabyteFileIsStreamedAndChunked()
    {
        AssertLoadTest(10, singleLine: false);
    }

    [Fact]
    public void HundredMegabyteFileLoadTest()
    {
        if (Environment.GetEnvironmentVariable("TEXTCHUNKER_LOAD_TESTS") != "1") return;
        AssertLoadTest(100, singleLine: false);
    }

    [Fact]
    public void OneGigabyteFileLoadTest()
    {
        if (Environment.GetEnvironmentVariable("TEXTCHUNKER_LOAD_TESTS") != "1GB") return;
        AssertLoadTest(1024, singleLine: false);
    }

    [Fact]
    public void SingleTenMegabyteLineLoadTest()
    {
        if (Environment.GetEnvironmentVariable("TEXTCHUNKER_LOAD_TESTS") is not ("1" or "1GB")) return;
        AssertLoadTest(10, singleLine: true);
    }
}
