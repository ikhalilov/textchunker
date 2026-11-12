using TextChunker.Abstractions;
using TextChunker.Chunking;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

/// <summary>
/// Tests for the `huggingface` model tokenizer (BPE, `tokenizer.json`).
/// The reference values were recorded by the `ModelCheck` probe on `google/embeddinggemma-2`:
/// a space is not a separate token, multiple spaces are collapsed,
/// CJK is counted via byte fallback (3 bytes = 3 tokens), and `GetIndexByTokenCount`
/// is 1-based (n == count → the length of the text).
/// Model-dependent tests are skipped if `tokenizer.json` is absent from the checkout.
/// </summary>
public sealed class HuggingFaceTokenizerTests
{
    private static readonly Lazy<HuggingFaceTokenizer?> Shared = new(() =>
    {
        string? path = FindTokenizerJson();
        return path is null ? null : new HuggingFaceTokenizer(path, null);
    });

    private static string? FindTokenizerJson()
    {
        DirectoryInfo? dir = new(AppContext.BaseDirectory);
        for (int depth = 0; depth < 10 && dir is not null; depth++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, "tokenizer.json");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        return null;
    }

    [Fact]
    public void NameAndModelAreReported()
    {
        HuggingFaceTokenizer? tok = Shared.Value;
        if (tok is null) return;

        Assert.Equal("huggingface", tok.Name);
        Assert.False(string.IsNullOrWhiteSpace(tok.ModelName));
    }

    [Fact]
    public void CountsMatchPinnedBpeProbe()
    {
        HuggingFaceTokenizer? tok = Shared.Value;
        if (tok is null) return;

        Assert.Equal(0, tok.CountTokens(""));
        Assert.Equal(2, tok.CountTokens("hello world"));
        Assert.Equal(2, tok.CountTokens("hello  world"));
        Assert.Equal(3, tok.CountTokens("a b c"));
        Assert.Equal(2, tok.CountTokens("a  b"));
        Assert.Equal(2, tok.CountTokens("hello\tworld"));
        Assert.Equal(2, tok.CountTokens("Hello World"));
        Assert.Equal(2, tok.CountTokens("one\n\n two"));
        // CJK via byte fallback: 3 characters * 3 bytes = 9 tokens.
        Assert.Equal(9, tok.CountTokens("\u65E5\u672C\u8A9E"));
    }

    [Fact]
    public void CountIsDeterministic()
    {
        HuggingFaceTokenizer? tok = Shared.Value;
        if (tok is null) return;

        const string text = "Точный подсчёт токенов: RU/EN + \u65E5\u672C\u8A9E + \U0001F680";
        Assert.Equal(tok.CountTokens(text), tok.CountTokens(text));
    }

    [Fact]
    public void CutAtTokenBoundaryMatchesPinnedProbe()
    {
        HuggingFaceTokenizer? tok = Shared.Value;
        if (tok is null) return;

        const string text = "hello world foo bar";
        Assert.Equal(0, tok.CutAtTokenBoundary(text, 0));
        Assert.Equal(5, tok.CutAtTokenBoundary(text, 1));
        Assert.Equal(11, tok.CutAtTokenBoundary(text, 2));
        Assert.Equal(15, tok.CutAtTokenBoundary(text, 3));
        Assert.Equal(text.Length, tok.CutAtTokenBoundary(text, 4));
        Assert.Equal(text.Length, tok.CutAtTokenBoundary(text, 99));
        Assert.Null(tok.CutAtTokenBoundary("", 1));
    }

    [Fact]
    public void FinalCountUsesCombinedTextNotSumOfParts()
    {
        HuggingFaceTokenizer? tok = Shared.Value;
        if (tok is null) return;

        // BPE: "hello wor"(2) + "ld"(1) are not additive; the total for "hello world" = 2 tokens.
        int parts = tok.CountTokens("hello wor") + tok.CountTokens("ld");
        int combined = ChunkTokenCount.ForFinalChunk(tok, "ld", "hello wor");
        Assert.Equal(tok.CountTokens("hello world"), combined);
        Assert.Equal(2, combined);
        Assert.NotEqual(parts, combined);
    }

    [Fact]
    public void PrefixAndTokenLimitHoldOnMixedCorpus()
    {
        HuggingFaceTokenizer? tok = Shared.Value;
        if (tok is null) return;

        const string prefix = "title: none | text: ";
        int prefixTokens = tok.CountTokens(prefix);
        var random = new Random(12345);
        string[] pieces =
        {
            "Привет", "мир", "hello", "world", "def foo", "\U0001F600", "\u65E5\u672C\u8A9E",
            "\n", "\n\n", ". ", "код", "123", "; ", "🚀"
        };

        for (int iteration = 0; iteration < 30; iteration++)
        {
            var builder = new System.Text.StringBuilder();
            int count = random.Next(5, 60);
            for (int i = 0; i < count; i++)
            {
                builder.Append(pieces[random.Next(pieces.Length)]);
            }
            string text = builder.ToString();
            int maxTokens = prefixTokens + random.Next(5, 30);

            var options = new ChunkingOptions(maxTokens: maxTokens, prefix: prefix);
            var writer = new CollectingWriter();
            var service = new ChunkingService(tok, ChunkerFactory.Create("recursive"), options);
            service.Process(new FakeSource(text.Split('\n')), writer);

            foreach (Chunk chunk in writer.Chunks)
            {
                int size = tok.CountTokens(prefix + chunk.Text);
                Assert.True(
                    size <= maxTokens,
                    $"iteration={iteration}: prefix+chunk={size} > max={maxTokens}, chunk='{chunk.Text}'");
            }
        }
    }

    [Fact]
    public void ChunkMetadataContainsTokenizerNameAndModel()
    {
        HuggingFaceTokenizer? tok = Shared.Value;
        if (tok is null) return;

        var writer = new CollectingWriter();
        var service = new ChunkingService(tok, new FixedChunker(), new ChunkingOptions(maxTokens: 40));
        service.Process(new FakeSource("hello world"), writer);

        Assert.NotEmpty(writer.Chunks);
        Assert.Equal("huggingface", writer.Chunks[0].Metadata["tokenizer"]);
        Assert.Equal(tok.ModelName, writer.Chunks[0].Metadata["tokenizerModel"]);
    }

    [Fact]
    public void InvalidTokenizerFileThrowsTokenizerLoadExceptionWithExitCode5()
    {
        string path = Path.Combine(Path.GetTempPath(), $"textchunker-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ this is not valid json");
        try
        {
            var ex = Assert.Throws<TokenizerLoadException>(() => new HuggingFaceTokenizer(path, null));
            Assert.Equal(5, ex.ExitCode);
            Assert.NotNull(ex.InnerException);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
