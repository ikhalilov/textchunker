using System.Text.Json;

using TextChunker.Abstractions;
using TextChunker.Chunking;
using TextChunker.Cli;
using TextChunker.Tokenizers;
using TextChunker.Writers;

namespace TextChunker.Tests;

/// <summary>
/// "Interrupted run" scenario (Ctrl+C): cancellation stops processing,
/// chunks already assembled are written out in full, and no partially written JSON lines remain.
/// </summary>
public sealed class CancellationTests
{
    private sealed class CancelAfterSource(IReadOnlyList<string> lines, CancellationTokenSource cancellation, int cancelAfterLines) : ITextSource
    {
        private readonly IReadOnlyList<string> _lines = lines;
        private readonly CancellationTokenSource _cancellation = cancellation;
        private readonly int _cancelAfterLines = cancelAfterLines;

        public string SourceName => "cancel-test";

        public bool BomDetected => false;

        public IEnumerable<(string Content, long StartChar, long EndChar)> Lines()
        {
            long start = 0;
            for (int i = 0; i < _lines.Count; i++)
            {
                if (i == _cancelAfterLines)
                {
                    _cancellation.Cancel();
                }

                string content = _lines[i];
                yield return (content, start, start + content.Length);
                start += content.Length + 1;
            }
        }
    }

    [Fact]
    public void ServiceCancellationFlushesCompletedChunksAndWritesOnlyWholeJsonLines()
    {
        using var cancellation = new CancellationTokenSource();
        string[] lines = [.. Enumerable.Range(0, 400).Select(i => $"line {i}: alpha beta gamma delta epsilon")];

        var source = new CancelAfterSource(lines, cancellation, cancelAfterLines: 40);
        var text = new StringWriter();
        var writer = new JsonLinesWriter(text);
        var options = new ChunkingOptions(strategy: "fixed", maxChars: 80, trim: false);
        var service = new ChunkingService(new WordTokenizer(), new FixedChunker(), options);

        var exception = Assert.Throws<OperationCanceledException>(
            () => service.Process(source, writer, cancellation.Token));
        Assert.Equal(cancellation.Token, exception.CancellationToken);

        string output = text.ToString();
        Assert.NotEmpty(output);
        Assert.EndsWith("\n", output);

        string[] jsonLines = output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.NotEmpty(jsonLines);
        foreach (string line in jsonLines)
        {
            using var document = JsonDocument.Parse(line);
            JsonElement chunk = document.RootElement;
            Assert.False(string.IsNullOrEmpty(chunk.GetProperty("text").GetString()));
            Assert.True(chunk.GetProperty("tokenCount").GetInt32() > 0);
        }
    }

    [Fact]
    public void ServiceCancellationBeforeAnyLineWritesNothing()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var source = new CancelAfterSource(new[] { "alpha", "beta" }, cancellation, cancelAfterLines: int.MaxValue);
        var text = new StringWriter();
        var options = new ChunkingOptions(strategy: "fixed", maxChars: 10);
        var service = new ChunkingService(new WordTokenizer(), new FixedChunker(), options);

        Assert.Throws<OperationCanceledException>(() => service.Process(source, new JsonLinesWriter(text), cancellation.Token));
        Assert.Empty(text.ToString());
    }

    [Fact]
    public void CliCancellationExitsZeroAndLeavesNoPartialRecord()
    {
        string input = Path.Combine(Path.GetTempPath(), $"textchunker-cancel-in-{Guid.NewGuid():N}.txt");
        string output = Path.Combine(Path.GetTempPath(), $"textchunker-cancel-out-{Guid.NewGuid():N}.jsonl");
        File.WriteAllText(input, string.Join('\n', Enumerable.Range(0, 100).Select(i => $"line {i} alpha beta gamma")));

        try
        {
            using var cancellation = new CancellationTokenSource();
            cancellation.Cancel();

            int exitCode = CliRunner.Run(
                new[]
                {
                    "--input", input,
                    "--output", output,
                    "--strategy", "fixed",
                    "--max-chars", "100",
                    "--tokenizer", "char"
                },
                cancellation.Token,
                configureConsole: false);

            Assert.Equal(0, exitCode);
            Assert.True(File.Exists(output));
            Assert.Equal(0, new FileInfo(output).Length);
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
}
