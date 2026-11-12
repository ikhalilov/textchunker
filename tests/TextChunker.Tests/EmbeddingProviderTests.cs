using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json;

using TextChunker.Abstractions.Embeddings;
using TextChunker.Embeddings;

namespace TextChunker.Tests;

/// <summary>Deterministic offline provider: hashing trick over words and n-grams.</summary>
public sealed class HashEmbeddingProviderTests
{
    private readonly HashEmbeddingProvider _provider = new();

    [Fact]
    public async Task SameTextProducesIdenticalVectors()
    {
        IReadOnlyList<float[]> first = await _provider.EmbedAsync(new[] { "привет мир" });
        IReadOnlyList<float[]> second = await _provider.EmbedAsync(new[] { "привет мир" });

        Assert.Equal(first[0], second[0]);
    }

    [Fact]
    public void VectorsAreL2Normalized()
    {
        float[] vector = _provider.Embed("кошка сидит на окне");
        double norm = Math.Sqrt(vector.Sum(value => (double)value * value));
        Assert.Equal(1.0, norm, 6);
    }

    [Fact]
    public void DimensionsAndBatchSizeAreReported()
    {
        var provider = new HashEmbeddingProvider(128);
        Assert.Equal(128, provider.Dimensions);
        Assert.Equal("hash", provider.Name);
        Assert.Equal(128, provider.Embed("текст").Length);
    }

    [Fact]
    public async Task BatchPreservesOrder()
    {
        var texts = new[] { "alpha", "beta beta", "gamma gamma gamma" };
        IReadOnlyList<float[]> vectors = await _provider.EmbedAsync(texts);

        Assert.Equal(3, vectors.Count);
        for (int i = 0; i < texts.Length; i++)
        {
            Assert.Equal(_provider.Embed(texts[i]), vectors[i]);
        }
    }

    [Fact]
    public void EmptyTextGetsDeterministicVector()
    {
        float[] first = _provider.Embed("");
        float[] second = _provider.Embed("   ");

        Assert.Equal(1f, first[0]);
        Assert.Equal(first, second);
    }

    [Fact]
    public void SimilarTextsAreCloserThanUnrelated()
    {
        float[] a = _provider.Embed("кошка сидит на окне");
        float[] b = _provider.Embed("кошка сидит на подоконнике");
        float[] c = _provider.Embed("фондовый рынок сегодня упал");

        Assert.True(Cosine(a, b) > Cosine(a, c), "похожие тексты должны быть ближе несвязанных");
    }

    [Fact]
    public void RejectsInvalidDimensions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new HashEmbeddingProvider(0));
    }

    private static double Cosine(float[] left, float[] right)
    {
        return left.Zip(right).Sum(pair => (double)pair.First * pair.Second);
    }
}

/// <summary>HTTP provider for llama.cpp: request format, batching, retries, response validation.</summary>
public sealed class LlamaCppEmbeddingProviderTests
{
    private static readonly EmbeddingOptions DefaultOptions = new()
    {
        Provider = "llama-cpp",
        Endpoint = "http://127.0.0.1:9932",
        Model = "embeddinggemma-2",
        Dimensions = 4,
        BatchSize = 32,
        MaxRetries = 2
    };

    private static LlamaCppEmbeddingProvider Create(HttpMessageHandler handler, EmbeddingOptions? options = null, string? apiKey = null)
        => new(options ?? DefaultOptions, apiKey, new HttpClient(handler));

    [Fact]
    public async Task PostsOpenAiCompatibleRequest()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));

        IReadOnlyList<float[]> vectors = await Create(handler).EmbedAsync(new[] { "привет мир" });

        Assert.Equal(1, handler.Calls);
        Assert.Equal("http://127.0.0.1:9932/v1/embeddings", handler.RequestUrls[0]);

        using JsonDocument request = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal("embeddinggemma-2", request.RootElement.GetProperty("model").GetString());
        Assert.Equal("привет мир", request.RootElement.GetProperty("input")[0].GetString());

        Assert.Equal(4, vectors[0].Length);
    }

    [Fact]
    public async Task OmitsModelWhenNotConfigured()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));
        var options = DefaultOptions with { Model = null };

        await Create(handler, options).EmbedAsync(new[] { "abc" });

        using JsonDocument request = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.False(request.RootElement.TryGetProperty("model", out _));
    }

    [Fact]
    public async Task NormalizesVectorsOnClientSide()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(new float[] { 2, 0, 0, 0 }));

        IReadOnlyList<float[]> vectors = await Create(handler).EmbedAsync(new[] { "abc" });

        Assert.Equal(1.0, Math.Sqrt(vectors[0].Sum(value => (double)value * value)), 6);
    }

    [Fact]
    public async Task OrdersResponseByIndex()
    {
        // The response arrives in reverse index order; the provider must restore the order.
        // The vectors are basis vectors so that L2 normalization does not change them.
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(
            HttpStatusCode.OK,
            "{\"object\":\"list\",\"data\":["
            + "{\"object\":\"embedding\",\"index\":1,\"embedding\":[0,0,1,0]},"
            + "{\"object\":\"embedding\",\"index\":0,\"embedding\":[1,0,0,0]}]}");

        IReadOnlyList<float[]> vectors = await Create(handler).EmbedAsync(new[] { "first", "second" });

        Assert.Equal(2, vectors.Count);
        Assert.Equal(1f, vectors[0][0]);
        Assert.Equal(1f, vectors[1][2]);
    }

    [Fact]
    public async Task SplitsIntoBatches()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0), Vec(0, 1, 0, 0)));
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(0, 0, 1, 0), Vec(0, 0, 0, 1)));
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 1, 1, 1)));
        var options = DefaultOptions with { BatchSize = 2 };

        IReadOnlyList<float[]> vectors = await Create(handler, options)
            .EmbedAsync(new[] { "a", "b", "c", "d", "e" });

        Assert.Equal(5, vectors.Count);
        Assert.Equal(3, handler.Calls);
    }

    [Fact]
    public async Task SendsBatchesInParallelWhenConcurrencyConfigured()
    {
        var handler = new ParallelTrackingHandler(expectedConcurrency: 2);
        var options = DefaultOptions with { BatchSize = 1, Concurrency = 2, Dimensions = 8 };

        IReadOnlyList<float[]> vectors = await Create(handler, options)
            .EmbedAsync(new[] { "t0", "t1", "t2", "t3" });

        Assert.Equal(4, vectors.Count);
        Assert.Equal(4, handler.Calls);
        Assert.Equal(2, handler.MaxActive);
    }

    [Fact]
    public async Task ParallelBatchesPreserveInputOrder()
    {
        var handler = new ParallelTrackingHandler(expectedConcurrency: 5);
        var options = DefaultOptions with { BatchSize = 1, Concurrency = 5, Dimensions = 8 };

        IReadOnlyList<float[]> vectors = await Create(handler, options)
            .EmbedAsync(new[] { "t0", "t1", "t2", "t3", "t4" });

        Assert.Equal(5, vectors.Count);
        for (int i = 0; i < vectors.Count; i++)
        {
            Assert.Equal(1f, vectors[i][i]);
        }
    }

    [Fact]
    public async Task SplitsBatchesByCharacterBudget()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0), Vec(0, 1, 0, 0)));
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(0, 0, 1, 0), Vec(0, 0, 0, 1)));
        var options = DefaultOptions with
        {
            BatchSize = 10,
            MaxBatchChars = 10,
            Dimensions = 4
        };

        IReadOnlyList<float[]> vectors = await Create(handler, options)
            .EmbedAsync(new[] { "aaaa", "bbbb", "cccc", "dddd" });

        Assert.Equal(2, handler.Calls);
        Assert.Equal(4, vectors.Count);
        using JsonDocument first = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal(2, first.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task SingleTextLongerThanBudgetIsSentAlone()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));
        var options = DefaultOptions with
        {
            BatchSize = 10,
            MaxBatchChars = 3
        };

        await Create(handler, options).EmbedAsync(new[] { "abcdef" });

        Assert.Equal(1, handler.Calls);
        using JsonDocument request = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal(1, request.RootElement.GetProperty("input").GetArrayLength());
    }

    [Fact]
    public async Task SplitsBatchWhenServerRejectsInputTooLarge()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, InputTooLargeBody());
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(0, 1, 0, 0)));
        var options = DefaultOptions with { BatchSize = 2, MaxRetries = 0 };

        IReadOnlyList<float[]> vectors = await Create(handler, options)
            .EmbedAsync(new[] { "first", "second" });

        Assert.Equal(3, handler.Calls);
        Assert.Equal(1f, vectors[0][0]);
        Assert.Equal(1f, vectors[1][1]);
    }

    [Fact]
    public async Task SplitsSingleOversizedTextAndNormalizesAverage()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, InputTooLargeBody());
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(0, 1, 0, 0)));
        var options = DefaultOptions with { BatchSize = 1, MaxRetries = 0 };
        string text = string.Join(" ", Enumerable.Repeat("word", 20));

        IReadOnlyList<float[]> vectors = await Create(handler, options)
            .EmbedAsync(new[] { text });

        Assert.Equal(3, handler.Calls);
        float[] vector = Assert.Single(vectors);
        Assert.Equal(4, vector.Length);
        Assert.Equal(1.0, Math.Sqrt(vector.Sum(value => (double)value * value)), 6);
    }

    [Fact]
    public async Task TinyTextRejectedByServerBecomesProviderError()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, InputTooLargeBody());
        var options = DefaultOptions with { BatchSize = 1, MaxRetries = 0 };

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => Create(handler, options).EmbedAsync(new[] { "short" }));

        Assert.Contains("не может обработать", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DuplicateTextsAreEmbeddedOnce()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0), Vec(0, 1, 0, 0)));

        IReadOnlyList<float[]> vectors = await Create(handler)
            .EmbedAsync(new[] { "same", "same", "other" });

        Assert.Equal(1, handler.Calls);
        using JsonDocument request = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal(2, request.RootElement.GetProperty("input").GetArrayLength());
        Assert.Equal(vectors[0], vectors[1]);
        Assert.NotEqual(vectors[0], vectors[2]);
    }

    [Fact]
    public async Task PrependsPromptToEveryText()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));
        var options = DefaultOptions with { Prompt = "title: none | text: " };

        await Create(handler, options).EmbedAsync(new[] { "abc" });

        using JsonDocument request = JsonDocument.Parse(handler.RequestBodies[0]!);
        Assert.Equal("title: none | text: abc", request.RootElement.GetProperty("input")[0].GetString());
    }

    [Fact]
    public async Task SendsApiKeyAsBearerHeader()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));

        await Create(handler, apiKey: "secret-token").EmbedAsync(new[] { "abc" });

        Assert.Equal("Bearer secret-token", handler.AuthHeaders[0]);
    }

    [Fact]
    public async Task RetriesTransientServerError()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.InternalServerError, "{}");
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));

        IReadOnlyList<float[]> vectors = await Create(handler).EmbedAsync(new[] { "abc" });

        Assert.Equal(2, handler.Calls);
        Assert.Equal(1f, vectors[0][0]);
    }

    [Fact]
    public async Task RetriesTooManyRequests()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.TooManyRequests, "{\"error\":\"busy\"}");
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));

        IReadOnlyList<float[]> vectors = await Create(handler).EmbedAsync(new[] { "abc" });

        Assert.Equal(2, handler.Calls);
        Assert.Equal(1f, vectors[0][0]);
    }

    [Fact]
    public async Task ThrowsProviderExceptionAfterRetriesExhausted()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "upstream down");
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "upstream down");
        handler.Enqueue(HttpStatusCode.ServiceUnavailable, "upstream down");
        var options = DefaultOptions with { MaxRetries = 1 };

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => Create(handler, options).EmbedAsync(new[] { "abc" }));

        Assert.Equal(2, handler.Calls);
        Assert.Equal(5, exception.ExitCode);
        Assert.Contains("503", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ClientErrorIsNotRetried()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.BadRequest, "{\"error\":\"bad request\"}");

        await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => Create(handler).EmbedAsync(new[] { "abc" }));

        Assert.Equal(1, handler.Calls);
    }

    [Fact]
    public async Task NetworkFailureBecomesProviderException()
    {
        var handler = new FakeEmbeddingHandler { FailWith = new HttpRequestException("connection refused") };
        var options = DefaultOptions with { MaxRetries = 0 };

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => Create(handler, options).EmbedAsync(new[] { "abc" }));

        Assert.IsType<HttpRequestException>(exception.InnerException);
    }

    [Fact]
    public async Task RequestTimeoutBecomesProviderException()
    {
        var options = DefaultOptions with
        {
            Timeout = TimeSpan.FromMilliseconds(100),
            MaxRetries = 0
        };
        using var client = new HttpClient(new HangingHandler()) { Timeout = options.Timeout };
        var provider = new LlamaCppEmbeddingProvider(options, apiKey: null, httpClient: client);

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => provider.EmbedAsync(new[] { "abc" }));

        Assert.Contains("Не удалось", exception.Message, StringComparison.Ordinal);
        Assert.Equal(5, exception.ExitCode);
    }

    [Fact]
    public async Task MalformedJsonBecomesProviderException()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, "{ это не json");

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => Create(handler).EmbedAsync(new[] { "abc" }));

        Assert.Contains("JSON", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MissingDataBecomesProviderException()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, "{\"object\":\"list\"}");

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => Create(handler).EmbedAsync(new[] { "abc" }));

        Assert.Contains("data", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WrongDimensionsBecomesProviderException()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(new float[] { 1, 2 }));

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => Create(handler).EmbedAsync(new[] { "abc" }));

        Assert.Contains("размерность", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CountMismatchBecomesProviderException()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));

        var exception = await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => Create(handler).EmbedAsync(new[] { "abc", "def" }));

        Assert.Contains("ожидалось", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task EmptyInputSkipsHttp()
    {
        var handler = new FakeEmbeddingHandler();
        IReadOnlyList<float[]> vectors = await Create(handler).EmbedAsync(Array.Empty<string>());

        Assert.Empty(vectors);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task CancellationPropagatesAsOperationCanceled()
    {
        var handler = new FakeEmbeddingHandler();
        handler.Enqueue(HttpStatusCode.OK, EmbeddingsResponse(Vec(1, 0, 0, 0)));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(handler).EmbedAsync(new[] { "abc" }, cancellation.Token));
    }

    [Fact]
    public void ValidatesOptionsOnConstruction()
    {
        var badEndpoint = DefaultOptions with { Endpoint = "not-a-url" };
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppEmbeddingProvider(badEndpoint));

        var badBatch = DefaultOptions with { BatchSize = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppEmbeddingProvider(badBatch));

        var badConcurrency = DefaultOptions with { Concurrency = 0 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppEmbeddingProvider(badConcurrency));

        var badBatchChars = DefaultOptions with { MaxBatchChars = -1 };
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppEmbeddingProvider(badBatchChars));

        var zeroTimeout = DefaultOptions with { Timeout = TimeSpan.Zero };
        Assert.Throws<ArgumentOutOfRangeException>(() => new LlamaCppEmbeddingProvider(zeroTimeout));
    }

    private static float[] Vec(float a, float b, float c, float d) => new[] { a, b, c, d };

    private static string InputTooLargeBody()
        => "{\"error\":{\"code\":500,\"message\":\"input (833 tokens) is too large to process. "
           + "increase the physical batch size (current batch size: 512)\"}}";

    private static string EmbeddingsResponse(params float[][] vectors)
    {
        var builder = new StringBuilder("{\"object\":\"list\",\"data\":[");
        for (int i = 0; i < vectors.Length; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }
            builder
                .Append("{\"object\":\"embedding\",\"index\":")
                .Append(i.ToString(CultureInfo.InvariantCulture))
                .Append(",\"embedding\":[")
                .Append(string.Join(",", vectors[i].Select(value => value.ToString("R", CultureInfo.InvariantCulture))))
                .Append("]}");
        }
        builder.Append("],\"usage\":{\"prompt_tokens\":0,\"total_tokens\":0}}");
        return builder.ToString();
    }

    /// <summary>Waits until the expected number of requests is in flight concurrently, then returns a basis vector.</summary>
    private sealed class ParallelTrackingHandler : HttpMessageHandler
    {
        private readonly int _expectedConcurrency;
        private readonly TaskCompletionSource _allStarted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _active;
        private int _maxActive;
        private int _calls;

        public ParallelTrackingHandler(int expectedConcurrency) => _expectedConcurrency = expectedConcurrency;

        public int Calls => Volatile.Read(ref _calls);

        public int MaxActive => Volatile.Read(ref _maxActive);

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _calls);
            int active = Interlocked.Increment(ref _active);
            UpdateMax(active);
            if (active >= _expectedConcurrency)
            {
                _allStarted.TrySetResult();
            }

            try
            {
                await _allStarted.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
                string body = await request.Content!.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                using JsonDocument document = JsonDocument.Parse(body);
                string text = document.RootElement.GetProperty("input")[0].GetString()!;
                int index = int.Parse(text.AsSpan(1), CultureInfo.InvariantCulture);

                var vector = new float[8];
                vector[index] = 1;
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(EmbeddingsResponse(vector), Encoding.UTF8, "application/json")
                };
            }
            finally
            {
                Interlocked.Decrement(ref _active);
            }
        }

        private void UpdateMax(int active)
        {
            int current;
            do
            {
                current = Volatile.Read(ref _maxActive);
                if (active <= current)
                {
                    return;
                }
            }
            while (Interlocked.CompareExchange(ref _maxActive, active, current) != current);
        }
    }

    /// <summary>The request "hangs" so that the timeout firing can be verified.</summary>
    private sealed class HangingHandler : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }

    private sealed class FakeEmbeddingHandler : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode Status, string Body)> _responses = new();

        public List<string?> RequestUrls { get; } = new();

        public List<string?> RequestBodies { get; } = new();

        public List<string?> AuthHeaders { get; } = new();

        public Exception? FailWith { get; set; }

        public int Calls => RequestUrls.Count;

        public void Enqueue(HttpStatusCode status, string body) => _responses.Enqueue((status, body));

        protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
            => Handle(request, cancellationToken);

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(Handle(request, cancellationToken));

        private HttpResponseMessage Handle(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUrls.Add(request.RequestUri?.ToString());
            AuthHeaders.Add(request.Headers.Authorization?.ToString());
            RequestBodies.Add(
                request.Content is null
                    ? string.Empty
                    : request.Content.ReadAsStringAsync(cancellationToken).GetAwaiter().GetResult());

            if (FailWith is not null)
            {
                throw FailWith;
            }

            (HttpStatusCode status, string body) = _responses.Count > 0
                ? _responses.Dequeue()
                : (HttpStatusCode.OK, "{\"object\":\"list\",\"data\":[]}");

            return new HttpResponseMessage(status)
            {
                Content = new StringContent(body, Encoding.UTF8, "application/json")
            };
        }
    }
}

/// <summary>Disk cache for embeddings and the caching decorator.</summary>
public sealed class EmbeddingCacheTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"textchunker-embed-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    [Fact]
    public void MissThenSetThenHit()
    {
        var cache = new EmbeddingCache("embeddinggemma-2", null, _root);
        float[] vector = { 0.5f, -0.5f, 0.25f };

        Assert.False(cache.TryGet("привет", out _));
        cache.Set("привет", vector);

        Assert.True(cache.TryGet("привет", out float[]? loaded));
        Assert.Equal(vector, loaded);
    }

    [Fact]
    public void StoresUnderSanitizedModelDirectory()
    {
        var cache = new EmbeddingCache("google/embeddinggemma-2", null, _root);
        cache.Set("abc", new float[] { 1, 2 });

        string[] directories = Directory.GetDirectories(_root);
        Assert.Single(directories);
        Assert.Equal("google_embeddinggemma-2", Path.GetFileName(directories[0]));
        Assert.Single(Directory.GetFiles(directories[0]));
    }

    [Fact]
    public void PromptIsPartOfTheKey()
    {
        var withoutPrompt = new EmbeddingCache("model", null, _root);
        var withPrompt = new EmbeddingCache("model", "title: x | ", _root);

        Assert.NotEqual(withoutPrompt.Hash("abc"), withPrompt.Hash("abc"));
    }

    [Fact]
    public void CorruptEntryIsDroppedAndRecomputed()
    {
        var cache = new EmbeddingCache("model", null, _root);
        cache.Set("abc", new float[] { 1, 2, 3 });
        string path = cache.GetPath("abc");

        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 });

        Assert.False(cache.TryGet("abc", out _));
        Assert.False(File.Exists(path));

        cache.Set("abc", new float[] { 4, 5, 6 });
        Assert.True(cache.TryGet("abc", out float[]? restored));
        Assert.Equal(new float[] { 4, 5, 6 }, restored);
    }

    [Fact]
    public void WrongLengthHeaderIsRejected()
    {
        var cache = new EmbeddingCache("model", null, _root);
        cache.Set("abc", new float[] { 1, 2, 3 });
        string path = cache.GetPath("abc");

        // Valid magic, but the body length does not match the declared dimension.
        using (var stream = File.Open(path, FileMode.Open, FileAccess.Write))
        using (var writer = new BinaryWriter(stream))
        {
            stream.Seek(5, SeekOrigin.Begin);
            writer.Write(42);
        }

        Assert.False(cache.TryGet("abc", out _));
    }

    [Fact]
    public void NoTmpFilesRemain()
    {
        var cache = new EmbeddingCache("model", null, _root);
        cache.Set("abc", new float[] { 1, 2 });

        Assert.Empty(Directory.GetFiles(_root, "*.tmp", SearchOption.AllDirectories));
    }

    [Fact]
    public void GetManyMixesHitsAndMisses()
    {
        var cache = new EmbeddingCache("model", null, _root);
        cache.Set("known", new float[] { 1, 2 });

        float[]?[] results = cache.GetMany(new[] { "known", "unknown" });

        Assert.Equal(new float[] { 1, 2 }, results[0]);
        Assert.Null(results[1]);
    }

    [Fact]
    public void EmptyVectorIsNotStored()
    {
        var cache = new EmbeddingCache("model", null, _root);
        cache.Set("abc", Array.Empty<float>());

        Assert.False(cache.TryGet("abc", out _));
    }

    [Fact]
    public void DefaultRootIsUnderLocalApplicationData()
    {
        string root = EmbeddingCache.DefaultRoot();
        Assert.Contains("TextChunker", root, StringComparison.Ordinal);
        Assert.Contains("embeddings", root, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CachedProviderCallsInnerOnlyForMisses()
    {
        var cache = new EmbeddingCache("model", null, _root);
        var spy = new SpyProvider();
        var provider = new CachedEmbeddingProvider(spy, cache);

        IReadOnlyList<float[]> first = await provider.EmbedAsync(new[] { "a", "b" });
        Assert.Single(spy.Calls);
        Assert.Equal(2, spy.Calls[0]);

        IReadOnlyList<float[]> second = await provider.EmbedAsync(new[] { "a", "b", "c" });
        Assert.Equal(2, spy.Calls.Count);
        Assert.Equal(1, spy.Calls[1]);

        Assert.Equal(2, provider.CacheHits);
        Assert.Equal(3, provider.CacheMisses);
        Assert.Equal(first[0], second[0]);
        Assert.Equal("spy", provider.Name);
        Assert.Equal(4, provider.Dimensions);
    }

    [Fact]
    public async Task CachedProviderHandlesDuplicateTexts()
    {
        var cache = new EmbeddingCache("model", null, _root);
        var spy = new SpyProvider();
        var provider = new CachedEmbeddingProvider(spy, cache);

        IReadOnlyList<float[]> vectors = await provider.EmbedAsync(new[] { "dup", "dup" });

        Assert.Equal(vectors[0], vectors[1]);
        Assert.Equal(2, spy.Calls[0]);
    }

    [Fact]
    public async Task CachedProviderRejectsShortResponse()
    {
        var cache = new EmbeddingCache("model", null, _root);
        var provider = new CachedEmbeddingProvider(new ShortResponseProvider(), cache);

        await Assert.ThrowsAsync<EmbeddingProviderException>(
            () => provider.EmbedAsync(new[] { "a", "b" }));
    }

    [Fact]
    public async Task CachedProviderIgnoresStaleDimension()
    {
        string staleRoot = Path.Combine(_root, "stale");
        var cache = new EmbeddingCache("model", null, staleRoot);
        cache.Set("abc", new float[] { 1, 2 }); // 2 dimensions, the provider expects 4

        var spy = new SpyProvider();
        var provider = new CachedEmbeddingProvider(spy, cache);

        IReadOnlyList<float[]> vectors = await provider.EmbedAsync(new[] { "abc" });

        Assert.Equal(4, vectors[0].Length);
        Assert.Single(spy.Calls);
    }

    private sealed class SpyProvider : IEmbeddingProvider
    {
        public List<int> Calls { get; } = new();

        public string Name => "spy";

        public int Dimensions => 4;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
        {
            Calls.Add(texts.Count);
            float[][] vectors = texts.Select(text => new[] { (float)text.Length, 1f, 2f, 3f }).ToArray();
            return Task.FromResult<IReadOnlyList<float[]>>(vectors);
        }
    }

    private sealed class ShortResponseProvider : IEmbeddingProvider
    {
        public string Name => "short";

        public int Dimensions => 4;

        public Task<IReadOnlyList<float[]>> EmbedAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken = default)
            => Task.FromResult<IReadOnlyList<float[]>>(new float[][] { new float[] { 1, 2, 3, 4 } });
    }
}

/// <summary>Provider factory: names, validation, cache, API key from the environment.</summary>
public sealed class EmbeddingProviderFactoryTests
{
    [Fact]
    public void CreatesHashProviderWrappedInCache()
    {
        IEmbeddingProvider provider = EmbeddingProviderFactory.Create(new EmbeddingOptions
        {
            Provider = "hash",
            CacheDir = Path.Combine(Path.GetTempPath(), $"textchunker-factory-{Guid.NewGuid():N}")
        });

        var cached = Assert.IsType<CachedEmbeddingProvider>(provider);
        Assert.IsType<HashEmbeddingProvider>(cached.Inner);
        Assert.Equal("hash", cached.Name);
        Assert.Equal(768, cached.Dimensions);
    }

    [Fact]
    public void CacheScopeIncludesEndpointModelAndDimensions()
    {
        string root = Path.Combine(Path.GetTempPath(), $"textchunker-scope-{Guid.NewGuid():N}");
        try
        {
            CachedEmbeddingProvider Create(string endpoint, string model, int dimensions)
                => (CachedEmbeddingProvider)EmbeddingProviderFactory.Create(new EmbeddingOptions
                {
                    Provider = "llama-cpp",
                    Endpoint = endpoint,
                    Model = model,
                    Dimensions = dimensions,
                    UseCache = true,
                    CacheDir = root
                });

            CachedEmbeddingProvider first = Create("http://127.0.0.1:9932", "same-model", 768);
            CachedEmbeddingProvider otherEndpoint = Create("http://127.0.0.1:9933", "same-model", 768);
            CachedEmbeddingProvider otherDimensions = Create("http://127.0.0.1:9932", "same-model", 512);
            CachedEmbeddingProvider same = Create("http://127.0.0.1:9932", "same-model", 768);

            Assert.NotNull(first.ScopeDirectory);
            Assert.StartsWith("same-model-", Path.GetFileName(first.ScopeDirectory));
            Assert.NotEqual(first.ScopeDirectory, otherEndpoint.ScopeDirectory);
            Assert.NotEqual(first.ScopeDirectory, otherDimensions.ScopeDirectory);
            Assert.Equal(first.ScopeDirectory, same.ScopeDirectory);
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
    public void UseCacheFalseReturnsBareProvider()
    {
        IEmbeddingProvider provider = EmbeddingProviderFactory.Create(new EmbeddingOptions
        {
            Provider = "hash",
            UseCache = false
        });

        Assert.IsType<HashEmbeddingProvider>(provider);
    }

    [Fact]
    public void CreatesLlamaCppProvider()
    {
        IEmbeddingProvider provider = EmbeddingProviderFactory.Create(new EmbeddingOptions
        {
            Provider = "llama-cpp",
            UseCache = false
        });

        var llama = Assert.IsType<LlamaCppEmbeddingProvider>(provider);
        Assert.Equal("llama-cpp", llama.Name);
        Assert.Equal("http://127.0.0.1:9932/v1/embeddings", llama.EmbeddingsUrlForTests);
    }

    [Fact]
    public void UnknownProviderThrowsWithSupportedNames()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(
            () => EmbeddingProviderFactory.Create(new EmbeddingOptions { Provider = "bogus" }));

        Assert.Contains("bogus", exception.Message, StringComparison.Ordinal);
        Assert.Contains("llama-cpp", exception.Message, StringComparison.Ordinal);
        Assert.Contains("hash", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void NamesAreCaseInsensitiveAndTrimmed()
    {
        IEmbeddingProvider provider = EmbeddingProviderFactory.Create(new EmbeddingOptions
        {
            Provider = "  HASH ",
            UseCache = false
        });

        Assert.IsType<HashEmbeddingProvider>(provider);
    }

    [Fact]
    public void ValidatesOptions()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmbeddingProviderFactory.Create(new EmbeddingOptions { Provider = "llama-cpp", Endpoint = "ftp://x", UseCache = false }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmbeddingProviderFactory.Create(new EmbeddingOptions { Provider = "hash", BatchSize = 0, UseCache = false }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => EmbeddingProviderFactory.Create(new EmbeddingOptions { Provider = "hash", MaxRetries = -1, UseCache = false }));
    }

    [Fact]
    public void SupportedNamesAndDefaults()
    {
        Assert.Equal(new[] { "llama-cpp", "hash" }, EmbeddingProviderFactory.SupportedNames);
        Assert.Equal("llama-cpp", EmbeddingProviderFactory.DefaultProviderName);
        Assert.Equal("TEXTCHUNKER_EMBEDDING_API_KEY", EmbeddingProviderFactory.ApiKeyEnvironmentVariable);
    }

    [Fact]
    public void NullOptionsThrows()
    {
        Assert.Throws<ArgumentNullException>(() => EmbeddingProviderFactory.Create(null!));
    }
}

/// <summary>Validation of semantic parameters.</summary>
public sealed class SemanticOptionsTests
{
    [Fact]
    public void DefaultsMatchDesign()
    {
        var options = new SemanticOptions();

        Assert.Equal(0.90, options.Percentile);
        Assert.Equal(0.02, options.MinDistance);
        Assert.Equal(1, options.Window);
        Assert.Equal(20_000_000, options.MaxBytes);
        Assert.Equal(SemanticFallbackMode.Recursive, options.Fallback);
        Assert.Equal(20, options.MinAtomChars);
        Assert.Equal(512, options.MaxAtomTokens);
        Assert.Equal(4_000_000, options.StreamWindowBytes);
        Assert.Equal(256_000, options.StreamOverlapBytes);
    }

    [Fact]
    public void AcceptsDefaults()
    {
        new SemanticOptions().Validate();
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-0.1)]
    [InlineData(1.5)]
    public void RejectsPercentileOutOfRange(double percentile)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticOptions { Percentile = percentile }.Validate());
    }

    [Fact]
    public void RejectsNegativeDistanceAndWindow()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticOptions { MinDistance = -0.01 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticOptions { Window = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticOptions { MaxBytes = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticOptions { MaxAtomTokens = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticOptions { StreamWindowBytes = 0 }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticOptions
        {
            StreamWindowBytes = 100,
            StreamOverlapBytes = -1
        }.Validate());
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticOptions
        {
            StreamWindowBytes = 100,
            StreamOverlapBytes = 100
        }.Validate());
    }

    [Theory]
    [InlineData(null, SemanticFallbackMode.Recursive)]
    [InlineData("recursive", SemanticFallbackMode.Recursive)]
    [InlineData(" RECURSIVE ", SemanticFallbackMode.Recursive)]
    [InlineData("error", SemanticFallbackMode.Error)]
    [InlineData("streaming", SemanticFallbackMode.Streaming)]
    [InlineData(" STREAMING ", SemanticFallbackMode.Streaming)]
    public void ParsesFallback(string? value, SemanticFallbackMode expected)
    {
        Assert.Equal(expected, SemanticOptions.ParseFallback(value));
    }

    [Fact]
    public void RejectsUnknownFallback()
    {
        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => SemanticOptions.ParseFallback("drop"));
        Assert.Contains("drop", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void EmbeddingOptionsDefaultsMatchDesign()
    {
        var options = new EmbeddingOptions();

        Assert.Equal("llama-cpp", options.Provider);
        Assert.Equal("http://127.0.0.1:9932", options.Endpoint);
        Assert.Equal(768, options.Dimensions);
        Assert.Equal(32, options.BatchSize);
        Assert.Equal(8_000, options.MaxBatchChars);
        Assert.Equal(1, options.Concurrency);
        Assert.True(options.UseCache);
        Assert.Equal(3, options.MaxRetries);
        Assert.Equal(TimeSpan.FromSeconds(120), options.Timeout);
        options.Validate();
    }
}
