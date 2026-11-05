using System.Text;

using TextChunker.Abstractions;
using TextChunker.Chunking;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

/// <summary>
/// Property-based инварианты нарезки: лимит не превышен, нет пустых чанков,
/// детерминированность, восстановление текста (fixed, trim=false, без overlap),
/// прогресс на случайных текстах (RU/EN/код/эмодзи/CJK) и параметрах.
/// </summary>
public sealed class ChunkInvariantTests
{
    private static readonly string[] Alphabet =
    [
        "a", "b", "c", "hello", "world", "Привет", "мир", "текст", " ", " ", " ", "\n", "\n\n",
        ".", ",", "!", "?", "😀", "🚀", "日本語", "abc", "123", "код", "test"
    ];

    private static string RandomText(Random random)
    {
        int pieces = random.Next(0, 40);
        var builder = new StringBuilder();
        for (int i = 0; i < pieces; i++)
        {
            builder.Append(Alphabet[random.Next(Alphabet.Length)]);
        }
        return builder.ToString();
    }

    private static ChunkingOptions RandomOptions(Random random, bool exactReconstruction)
    {
        string strategy = random.Next(3) switch
        {
            0 => "fixed",
            1 => "recursive",
            _ => "paragraph"
        };

        bool byChars = random.Next(2) == 0;
        string? prefix = random.Next(3) == 0 ? "title: x | text: " : null;
        int prefixTokens = prefix is null ? 0 : new WordTokenizer().CountTokens(prefix);
        int max = byChars
            ? random.Next(4, 60)
            : random.Next(prefixTokens + 1, prefixTokens + 20);
        int? overlap = null;
        if (!exactReconstruction && random.Next(3) == 0)
        {
            overlap = byChars ? random.Next(1, max) : random.Next(1, Math.Max(1, max - prefixTokens));
            if (overlap <= 0 || overlap >= max)
            {
                overlap = null;
            }
        }

        return new ChunkingOptions(
            strategy: strategy,
            maxChars: byChars ? max : null,
            maxTokens: byChars ? null : max,
            overlapChars: byChars ? overlap : null,
            overlapTokens: byChars ? null : overlap,
            trim: !exactReconstruction,
            prefix: prefix);
    }

    private static (ChunkingStats Stats, CollectingWriter Writer) RunService(
        string text,
        ChunkingOptions options,
        ITokenizer tokenizer)
    {
        var writer = new CollectingWriter();
        var service = new ChunkingService(tokenizer, ChunkerFactory.Create(options.Strategy), options);
        string[] lines = text.Split('\n');
        ChunkingStats stats = service.Process(new FakeSource(lines), writer);
        return (stats, writer);
    }

    [Fact]
    public void EveryChunkRespectsTheLimit()
    {
        var random = new Random(20261103);
        for (int iteration = 0; iteration < 300; iteration++)
        {
            string text = RandomText(random);
            ChunkingOptions options = RandomOptions(random, exactReconstruction: false);
            var tokenizer = new WordTokenizer();

            (_, CollectingWriter writer) = RunService(text, options, tokenizer);

            foreach (Chunk chunk in writer.Chunks)
            {
                Assert.NotEmpty(chunk.Text);
                int size = options.Unit == SizeUnit.Chars
                    ? chunk.Text.Length
                    : options.Prefix is null
                        ? tokenizer.CountTokens(chunk.Text)
                        : tokenizer.CountTokens(options.Prefix + chunk.Text);
                Assert.True(
                    size <= options.MaxSize,
                    $"iteration={iteration}: size={size} max={options.MaxSize} unit={options.Unit} strategy={options.Strategy} text={Escape(text)} chunk={Escape(chunk.Text)}");
            }
        }
    }

    [Fact]
    public void FixedWithoutTrimReconstructsTheInput()
    {
        var random = new Random(777);
        for (int iteration = 0; iteration < 150; iteration++)
        {
            string text = RandomText(random);
            var options = new ChunkingOptions(
                strategy: "fixed",
                maxChars: random.Next(4, 40),
                trim: false);

            (_, CollectingWriter writer) = RunService(text, options, new CharTokenizer());

            string reconstructed = string.Concat(writer.Chunks.Select(c => c.Text));
            Assert.Equal(text, reconstructed);
        }
    }

    [Fact]
    public void ProcessingIsDeterministic()
    {
        var random = new Random(424242);
        for (int iteration = 0; iteration < 80; iteration++)
        {
            string text = RandomText(random);
            ChunkingOptions options = RandomOptions(random, exactReconstruction: false);

            (_, CollectingWriter first) = RunService(text, options, new WordTokenizer());
            (_, CollectingWriter second) = RunService(text, options, new WordTokenizer());

            Assert.Equal(
                first.Chunks.Select(c => (c.Id, c.Seq, c.Text, c.StartChar, c.EndChar, c.TokenCount)),
                second.Chunks.Select(c => (c.Id, c.Seq, c.Text, c.StartChar, c.EndChar, c.TokenCount)));
        }
    }

    [Fact]
    public void OffsetsAreMonotonicAndInsideInput()
    {
        var random = new Random(9001);
        for (int iteration = 0; iteration < 150; iteration++)
        {
            string text = RandomText(random);
            ChunkingOptions options = RandomOptions(random, exactReconstruction: false);

            (_, CollectingWriter writer) = RunService(text, options, new WordTokenizer());

            long previousStart = -1;
            foreach (Chunk chunk in writer.Chunks)
            {
                Assert.NotNull(chunk.StartChar);
                Assert.NotNull(chunk.EndChar);
                Assert.True(chunk.StartChar >= 0);
                Assert.True(chunk.EndChar >= chunk.StartChar);
                Assert.True(chunk.EndChar <= text.Length);
                Assert.True(chunk.StartChar >= previousStart, "startChar должен не убывать");
                previousStart = chunk.StartChar.Value;
            }
        }
    }

    private static string Escape(string value)
    {
        return value.Replace("\n", "\\n", StringComparison.Ordinal);
    }
}
