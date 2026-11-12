namespace TextChunker.Api.Contracts;

/// <summary>
/// Embedding parameters for <c>strategy = "semantic"</c>. An API key is not accepted here:
/// it is read only from the server environment variable.
/// </summary>
public sealed record EmbeddingRequest
{
    /// <summary>llama-cpp | hash (default: llama-cpp).</summary>
    public string? Provider { get; init; }

    /// <summary>Embedding server URL (default: http://127.0.0.1:9932).</summary>
    public string? Endpoint { get; init; }

    /// <summary>Model name sent to the embedding server.</summary>
    public string? Model { get; init; }

    /// <summary>Embedding batch size (default: 32).</summary>
    public int? BatchSize { get; init; }

    /// <summary>Batch character budget (conservative token estimate; 0 — no limit).</summary>
    public int? MaxBatchChars { get; init; }

    /// <summary>How many batches to send in parallel (1 — sequential).</summary>
    public int? Concurrency { get; init; }

    /// <summary>HTTP timeout for server requests, seconds (default: 120).</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Retries for network errors and 429/5xx (default: 3).</summary>
    public int? MaxRetries { get; init; }

    /// <summary>Prefix prompt added to each atom before embedding.</summary>
    public string? Prompt { get; init; }

    /// <summary>Embedding cache directory (default: LocalApplicationData/TextChunker/embeddings).</summary>
    public string? CacheDir { get; init; }

    /// <summary>Use the on-disk embedding cache (default: true).</summary>
    public bool? UseCache { get; init; }

    /// <summary>Expected vector dimension (default: 768; 0 — do not validate).</summary>
    public int? Dimensions { get; init; }
}

/// <summary>Semantic chunking parameters.</summary>
public sealed record SemanticRequest
{
    /// <summary>Percentile of 1 − cos distances used for boundaries, (0, 1] (default: 0.90).</summary>
    public double? Percentile { get; init; }

    /// <summary>Minimum boundary distance (default: 0.02).</summary>
    public double? MinDistance { get; init; }

    /// <summary>TextTiling window: 1 — adjacent atoms (default: 1).</summary>
    public int? Window { get; init; }

    /// <summary>Input size limit in document mode, bytes (default: 20000000).</summary>
    public long? MaxBytes { get; init; }

    /// <summary>Streaming-mode window size in bytes (default: 4000000).</summary>
    public int? StreamWindowBytes { get; init; }

    /// <summary>Overlap between adjacent streaming windows in bytes (default: 256000).</summary>
    public int? StreamOverlapBytes { get; init; }

    /// <summary>
    /// Behavior when <c>maxBytes</c> is exceeded: <c>recursive</c> — fall back, <c>error</c> — 413,
    /// <c>streaming</c> — windowed processing (default: recursive).
    /// </summary>
    public string? Fallback { get; init; }

    /// <summary>Merge atoms shorter than N characters (default: 20).</summary>
    public int? MinAtomChars { get; init; }

    /// <summary>Pre-split atoms longer than N tokens (default: 512).</summary>
    public int? MaxAtomTokens { get; init; }
}
