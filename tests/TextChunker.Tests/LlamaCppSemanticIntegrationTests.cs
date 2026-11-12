using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;
using TextChunker.Chunking;
using TextChunker.Chunking.Semantic;
using TextChunker.Embeddings;
using TextChunker.Tokenizers;

namespace TextChunker.Tests;

/// <summary>
/// Integration with a live llama.cpp. Enabled by the environment variable
/// <c>TEXTCHUNKER_LLAMA_CPP_URL</c> (for example, <c>http://127.0.0.1:9932</c>);
/// without it the tests are skipped, so CI runs without a server.
/// </summary>
public sealed class LlamaCppSemanticIntegrationTests
{
    private const string UrlVariable = "TEXTCHUNKER_LLAMA_CPP_URL";

    private static string? Endpoint => Environment.GetEnvironmentVariable(UrlVariable);

    private static IEmbeddingProvider CreateProvider(string endpoint)
        => EmbeddingProviderFactory.Create(new EmbeddingOptions
        {
            Provider = "llama-cpp",
            Endpoint = endpoint,
            Model = Environment.GetEnvironmentVariable("TEXTCHUNKER_LLAMA_CPP_MODEL"),
            Dimensions = 768,
            BatchSize = 16,
            UseCache = false,
            Timeout = TimeSpan.FromSeconds(120),
            MaxRetries = 1
        });

    private const string Document = """
        # Установка

        Сначала скачайте модель и распакуйте архив. Затем проверьте контрольные суммы.

        ## Настройка сервера

        Запустите llama-server с флагом --embedding и укажите порт.

        Фондовый рынок сегодня упал на три процента. Инвесторы фиксируют убытки.

        Bring water to a boil, add salt, and cook the pasta for nine minutes.
        """;

    [Fact]
    public async Task LiveServerReturnsNormalizedVectors()
    {
        string? endpoint = Endpoint;
        if (endpoint is null)
        {
            return;
        }

        IEmbeddingProvider provider = CreateProvider(endpoint);
        IReadOnlyList<float[]> vectors = await provider.EmbedAsync(new[] { "привет мир", "hello world" });

        Assert.Equal(2, vectors.Count);
        Assert.All(vectors, vector => Assert.Equal(768, vector.Length));
        foreach (float[] vector in vectors)
        {
            double norm = Math.Sqrt(vector.Sum(value => (double)value * value));
            Assert.Equal(1.0, norm, 4);
        }
    }

    [Fact]
    public async Task LiveServerProducesDeterministicBoundaries()
    {
        string? endpoint = Endpoint;
        if (endpoint is null)
        {
            return;
        }

        var tokenizer = new WordTokenizer();
        var options = new SemanticOptions { MinAtomChars = 0, Window = 1, Percentile = 0.9, MinDistance = 0.02 };

        SemanticPlan first = await new SemanticPlanner(CreateProvider(endpoint), options)
            .PlanAsync(Document, tokenizer);
        SemanticPlan second = await new SemanticPlanner(CreateProvider(endpoint), options)
            .PlanAsync(Document, tokenizer);

        Assert.NotEmpty(first.Atoms);
        Assert.Equal(first.Breakpoints, second.Breakpoints);
        Assert.All(first.Breakpoints, index => Assert.InRange(index, 1, first.Atoms.Count - 1));
    }

    [Fact]
    public async Task LiveServerEndToEndRespectsTokenLimit()
    {
        string? endpoint = Endpoint;
        if (endpoint is null)
        {
            return;
        }

        var tokenizer = new WordTokenizer();
        var semantic = new SemanticOptions { MinAtomChars = 0 };
        var chunking = new ChunkingOptions(strategy: "semantic", maxTokens: 40);

        var writer = new CollectingWriter();
        var service = new ChunkingService(
            tokenizer,
            new SemanticChunker(),
            chunking,
            semanticPlanner: new SemanticPlanner(CreateProvider(endpoint), semantic),
            semanticOptions: semantic);

        await service.ProcessAsync(new FakeSource(Document.Split('\n')), writer);

        Assert.NotEmpty(writer.Chunks);
        foreach (Chunk chunk in writer.Chunks)
        {
            Assert.True(tokenizer.CountTokens(chunk.Text) <= 40, $"'{chunk.Text}'");
            Assert.Equal("llama-cpp", chunk.Metadata["semanticProvider"]);
        }
    }
}
