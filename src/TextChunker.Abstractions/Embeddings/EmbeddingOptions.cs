namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// Embedding provider settings. The defaults match a local llama.cpp server with
/// <c>embeddinggemma-2</c> (see docs/semantic-chunking.md).
/// </summary>
public sealed record EmbeddingOptions
{
    /// <summary>Provider: <c>llama-cpp</c> | <c>hash</c>.</summary>
    public string Provider { get; init; } = "llama-cpp";

    /// <summary>Base URL of the embeddings server (used only for <c>llama-cpp</c>).</summary>
    public string Endpoint { get; init; } = "http://127.0.0.1:9932";

    /// <summary>Model name sent in requests; null/empty means the <c>model</c> field is not sent.</summary>
    public string? Model { get; init; }

    /// <summary>Expected vector dimension (0 — do not validate).</summary>
    public int Dimensions { get; init; } = 768;

    /// <summary>Batch size for server requests.</summary>
    public int BatchSize { get; init; } = 32;

    /// <summary>
    /// Character budget per batch (a conservative token estimate): the batch is closed
    /// once the combined text length exceeds the limit. 0 — limit by BatchSize only.
    /// </summary>
    public int MaxBatchChars { get; init; } = 8_000;

    /// <summary>
    /// How many batches to send in parallel (1 — sequential).
    /// Useful with a server that has several slots (<c>--parallel N</c>).
    /// </summary>
    public int Concurrency { get; init; } = 1;

    /// <summary>Prefix prompt prepended to each text (null — no prompt). Included in the cache key.</summary>
    public string? Prompt { get; init; }

    /// <summary>Embedding cache directory; null — the default path.</summary>
    public string? CacheDir { get; init; }

    /// <summary>false — disable the on-disk cache.</summary>
    public bool UseCache { get; init; } = true;

    /// <summary>Maximum cache size in bytes; null — no limit.</summary>
    public long? CacheMaxBytes { get; init; }

    /// <summary>Delete cache entries older than N days; null — no TTL.</summary>
    public int? CacheTtlDays { get; init; }

    /// <summary>HTTP request timeout.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Number of retries on network errors and 429/5xx.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// API key. Populated by the factory from an environment variable; the key is never
    /// accepted from the request body (CLI/API).
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>Validate parameter consistency (invalid values → ArgumentException, exit 2 / 400).</summary>
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Provider))
        {
            throw new ArgumentOutOfRangeException(nameof(Provider), Provider, "provider эмбеддингов не задан.");
        }

        if (BatchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(BatchSize), BatchSize, "--embedding-batch-size должен быть не меньше 1.");
        }

        if (Concurrency < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Concurrency),
                Concurrency,
                "--embedding-concurrency должен быть не меньше 1.");
        }

        if (MaxBatchChars < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaxBatchChars),
                MaxBatchChars,
                "--embedding-max-batch-chars не может быть отрицательным.");
        }

        if (Dimensions < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(Dimensions), Dimensions, "Размерность эмбеддингов не может быть отрицательной.");
        }

        if (MaxRetries < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxRetries), MaxRetries, "Число повторов не может быть отрицательным.");
        }

        if (Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(Timeout), Timeout, "Таймаут эмбеддингов должен быть больше нуля.");
        }

        if (CacheMaxBytes is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CacheMaxBytes),
                CacheMaxBytes,
                "--embedding-cache-max-bytes не может быть отрицательным.");
        }

        if (CacheTtlDays is < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(CacheTtlDays),
                CacheTtlDays,
                "--embedding-cache-ttl-days не может быть отрицательным.");
        }

        string key = Provider.Trim().ToLowerInvariant();
        if (key == "llama-cpp")
        {
            if (!Uri.TryCreate(Endpoint, UriKind.Absolute, out Uri? uri)
                || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(Endpoint),
                    Endpoint,
                    "--embedding-endpoint должен быть абсолютным http(s)-URL.");
            }
        }
    }
}
