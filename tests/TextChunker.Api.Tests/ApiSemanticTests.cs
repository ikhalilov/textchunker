using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace TextChunker.Api.Tests;

/// <summary>Семантическая стратегия через API (офлайн-провайдер hash).</summary>
public sealed class ApiSemanticTests(ApiServerFixture fixture) : IClassFixture<ApiServerFixture>
{
    private readonly ApiServerFixture _fixture = fixture;

    private ApiServer Server => _fixture.Server;

    private static MultipartFormDataContent FileForm(params (string Key, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(
            "# Topic\n\nOne sentence. Another sentence.\n\nDifferent topic. More details.")),
            "file", "semantic.md");
        form.Add(new StringContent("semantic"), "strategy");
        form.Add(new StringContent("word"), "tokenizer");
        form.Add(new StringContent("6"), "maxTokens");
        foreach (var (key, value) in fields) form.Add(new StringContent(value), key);
        return form;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UploadedMarkdownUsesSemanticOptions(bool jsonFields)
    {
        using var form = jsonFields
            ? FileForm(("embedding", "{\"provider\":\"hash\",\"useCache\":false}"),
                ("semantic", "{\"percentile\":0.8,\"window\":1}"))
            : FileForm(("embeddingProvider", "hash"), ("embeddingUseCache", "false"),
                ("semanticPercentile", "0.8"), ("semanticWindow", "1"));
        using var response = await Server.Client.PostAsync("/api/v1/chunk/file", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var chunks = document.RootElement.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            Assert.True(chunk.GetProperty("tokenCount").GetInt32() <= 6);
            var metadata = chunk.GetProperty("metadata");
            Assert.Equal("hash", metadata.GetProperty("semanticProvider").GetString());
            Assert.Equal(0.8, metadata.GetProperty("breakpointPercentile").GetDouble());
        });
    }

    [Fact]
    public async Task UploadedSemanticDocumentHonorsSizeLimit()
    {
        using var form = FileForm(("embeddingProvider", "hash"), ("embeddingUseCache", "false"),
            ("semanticMaxBytes", "10"), ("semanticFallback", "error"));
        using var response = await Server.Client.PostAsync("/api/v1/chunk/file", form);
        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Theory]
    [InlineData("embedding", "{")]
    [InlineData("semantic", "[]")]
    [InlineData("semantic", "null")]
    public async Task UploadedOptionsRejectMalformedJson(string field, string value)
    {
        using var form = FileForm((field, value));
        using var response = await Server.Client.PostAsync("/api/v1/chunk/file", form);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FlatFieldsOverrideJsonOptions()
    {
        using var form = FileForm(("embedding", "{\"provider\":\"bogus\"}"),
            ("embeddingProvider", "hash"), ("embeddingUseCache", "false"),
            ("semantic", "{\"percentile\":2}"), ("semanticPercentile", "0.8"));
        using var response = await Server.Client.PostAsync("/api/v1/chunk/file", form);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    private static object SemanticBody(string text, object? semantic = null) => new
    {
        text,
        strategy = "semantic",
        tokenizer = "word",
        maxTokens = 40,
        embedding = new { provider = "hash", useCache = false },
        semantic = semantic ?? new { percentile = 0.9, minDistance = 0.02, window = 1 }
    };

    [Fact]
    public async Task SemanticStrategyReturnsChunksWithMetadata()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            SemanticBody("Установка сервера. Запустите llama-server. Фондовый рынок упал. Инвесторы продают."));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        JsonElement[] chunks = body.GetProperty("chunks").EnumerateArray().ToArray();

        Assert.NotEmpty(chunks);
        Assert.All(chunks, chunk =>
        {
            JsonElement metadata = chunk.GetProperty("metadata");
            Assert.Equal("semantic", metadata.GetProperty("strategy").GetString());
            Assert.Equal("hash", metadata.GetProperty("semanticProvider").GetString());
            Assert.Equal(0.9, metadata.GetProperty("breakpointPercentile").GetDouble());
        });
        Assert.Equal(chunks.Length, body.GetProperty("stats").GetProperty("chunkCount").GetInt32());
    }

    [Fact]
    public async Task SemanticStrategyRespectsTokenLimit()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            new
            {
                text = "One two three four five six seven eight nine ten.",
                strategy = "semantic",
                tokenizer = "word",
                maxTokens = 4,
                embedding = new { provider = "hash", useCache = false }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        foreach (JsonElement chunk in body.GetProperty("chunks").EnumerateArray())
        {
            Assert.True(chunk.GetProperty("tokenCount").GetInt32() <= 4);
        }
    }

    [Fact]
    public async Task OversizedDocumentWithErrorFallbackReturns413()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            SemanticBody(new string('a', 500), new { maxBytes = 50, fallback = "error" }));

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnreachableProviderReturns503()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            new
            {
                text = "One. Two. Three.",
                strategy = "semantic",
                tokenizer = "word",
                maxTokens = 20,
                embedding = new
                {
                    provider = "llama-cpp",
                    endpoint = "http://127.0.0.1:1",
                    useCache = false
                }
            });

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownEmbeddingProviderReturns400()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            new
            {
                text = "One. Two.",
                strategy = "semantic",
                tokenizer = "word",
                maxTokens = 20,
                embedding = new { provider = "bogus" }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SemanticStreamReturnsChunks()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chunk/stream")
        {
            Content = JsonContent.Create(SemanticBody(
                "Установка сервера. Запустите llama-server. Фондовый рынок упал. Инвесторы продают."))
        };

        using HttpResponseMessage response = await Server.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        string payload = await response.Content.ReadAsStringAsync();
        JsonElement[] chunks = payload
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonDocument.Parse(line).RootElement)
            .ToArray();

        Assert.NotEmpty(chunks);
        foreach (JsonElement chunk in chunks)
        {
            Assert.True(chunk.GetProperty("tokenCount").GetInt32() <= 40);
            JsonElement metadata = chunk.GetProperty("metadata");
            Assert.Equal("semantic", metadata.GetProperty("strategy").GetString());
            Assert.Equal("hash", metadata.GetProperty("semanticProvider").GetString());
        }
    }

    [Fact]
    public async Task SemanticStreamProviderFailureReturns503()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chunk/stream")
        {
            Content = JsonContent.Create(new
            {
                text = "One. Two. Three.",
                strategy = "semantic",
                tokenizer = "word",
                maxTokens = 20,
                embedding = new
                {
                    provider = "llama-cpp",
                    endpoint = "http://127.0.0.1:1",
                    useCache = false,
                    maxRetries = 0
                }
            })
        };

        using HttpResponseMessage response = await Server.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SemanticStreamOversizedReturns413()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chunk/stream")
        {
            Content = JsonContent.Create(SemanticBody(
                new string('a', 500),
                new { maxBytes = 50, fallback = "error" }))
        };

        using HttpResponseMessage response = await Server.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task StreamingFallbackReturnsChunks()
    {
        string text = string.Join("\n", Enumerable.Range(0, 20)
            .Select(i => $"Строка {i}: длинное предложение для проверки потоковых окон."));

        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            new
            {
                text,
                strategy = "semantic",
                tokenizer = "word",
                maxTokens = 20,
                embedding = new { provider = "hash", useCache = false },
                semantic = new
                {
                    fallback = "streaming",
                    maxBytes = 10,
                    streamWindowBytes = 200,
                    streamOverlapBytes = 50
                }
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        JsonElement[] chunks = body.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.NotEmpty(chunks);
        for (int i = 0; i < chunks.Length; i++)
        {
            Assert.Equal(i + 1, chunks[i].GetProperty("seq").GetInt32());
            Assert.Equal(
                "streaming",
                chunks[i].GetProperty("metadata").GetProperty("semanticFallback").GetString());
        }
    }

    [Fact]
    public async Task InvalidSemanticPercentileReturns400()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            SemanticBody("One. Two.", new { percentile = 2.0 }));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidEmbeddingConcurrencyReturns400()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            new
            {
                text = "One. Two.",
                strategy = "semantic",
                tokenizer = "word",
                maxTokens = 20,
                embedding = new { provider = "hash", concurrency = 0 }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidEmbeddingTimeoutReturns400()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            new
            {
                text = "One. Two.",
                strategy = "semantic",
                tokenizer = "word",
                maxTokens = 20,
                embedding = new { provider = "hash", timeoutSeconds = 0 }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task InvalidEmbeddingMaxBatchCharsReturns400()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            new
            {
                text = "One. Two.",
                strategy = "semantic",
                tokenizer = "word",
                maxTokens = 20,
                embedding = new { provider = "hash", maxBatchChars = -1 }
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
