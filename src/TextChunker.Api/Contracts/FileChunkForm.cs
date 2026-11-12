using System.Text.Json;

namespace TextChunker.Api.Contracts;

/// <summary>
/// multipart form fields for file upload (<c>POST /api/chunk/file</c>).
/// They mirror <see cref="ChunkRequest"/>, but arrive as form data.
/// </summary>
public sealed class FileChunkForm
{
    /// <summary>Strategy: fixed | recursive | paragraph | semantic (default: recursive).</summary>
    public string? Strategy { get; set; }
    /// <summary>Tokenizer: none | char | word | regex | huggingface (default: word).</summary>
    public string? Tokenizer { get; set; }
    /// <summary>HuggingFace model or path to tokenizer.json.</summary>
    public string? TokenizerModel { get; set; }
    /// <summary>Explicit path to a local tokenizer.json (takes precedence over TokenizerModel).</summary>
    public string? TokenizerPath { get; set; }
    /// <summary>tokenizer.json cache directory.</summary>
    public string? TokenizerCacheDir { get; set; }
    /// <summary>Task prefix counted toward the token limit.</summary>
    public string? Prefix { get; set; }

    /// <summary>Maximum tokens per chunk (default: 512 when no limit is specified).</summary>
    public int? MaxTokens { get; set; }
    /// <summary>Maximum characters per chunk.</summary>
    public int? MaxChars { get; set; }
    /// <summary>Maximum words per chunk.</summary>
    public int? MaxWords { get; set; }
    /// <summary>Maximum UTF-8 bytes per chunk.</summary>
    public int? MaxBytes { get; set; }

    /// <summary>Minimum tokens for the final chunk.</summary>
    public int? MinTokens { get; set; }
    /// <summary>Minimum characters for the final chunk.</summary>
    public int? MinChars { get; set; }
    /// <summary>Minimum words for the final chunk.</summary>
    public int? MinWords { get; set; }

    /// <summary>Overlap between adjacent chunks in tokens.</summary>
    public int? OverlapTokens { get; set; }
    /// <summary>Overlap between adjacent chunks in characters.</summary>
    public int? OverlapChars { get; set; }

    /// <summary>Trim whitespace at chunk edges (default: true).</summary>
    public bool? Trim { get; set; }
    /// <summary>Drop the final chunk when it is smaller than the minimum.</summary>
    public bool? DropSmall { get; set; }

    /// <summary>JSON array of strings, for example <c>["\n\n","\n"]</c>.</summary>
    public string? Separators { get; set; }

    /// <summary>JSON object of user metadata.</summary>
    public string? Metadata { get; set; }

    /// <summary>Source label for the top-level source and id (default: file name).</summary>
    public string? Source { get; set; }

    /// <summary>File encoding (default: utf-8; code pages are supported).</summary>
    public string? Encoding { get; set; }

    /// <summary>replace (default) or error.</summary>
    public string? InvalidEncoding { get; set; }

    /// <summary>JSON objects with the same fields as in the JSON endpoint.</summary>
    public string? Embedding { get; set; }
    /// <summary>JSON object of semantic boundary parameters (like the semantic field in /api/chunk).</summary>
    public string? Semantic { get; set; }

    /// <summary>Embedding provider: llama-cpp | hash.</summary>
    public string? EmbeddingProvider { get; set; }
    /// <summary>Embedding server URL.</summary>
    public string? EmbeddingEndpoint { get; set; }
    /// <summary>Model name sent to the embedding server.</summary>
    public string? EmbeddingModel { get; set; }
    /// <summary>Embedding batch size.</summary>
    public int? EmbeddingBatchSize { get; set; }
    /// <summary>Batch character budget (0 — count only).</summary>
    public int? EmbeddingMaxBatchChars { get; set; }
    /// <summary>How many batches to send in parallel.</summary>
    public int? EmbeddingConcurrency { get; set; }
    /// <summary>HTTP timeout for embedding server requests, seconds.</summary>
    public int? EmbeddingTimeoutSeconds { get; set; }
    /// <summary>Retries for network errors and 429/5xx.</summary>
    public int? EmbeddingMaxRetries { get; set; }
    /// <summary>Prefix prompt added to each atom.</summary>
    public string? EmbeddingPrompt { get; set; }
    /// <summary>Embedding cache directory.</summary>
    public string? EmbeddingCacheDir { get; set; }
    /// <summary>Use the on-disk embedding cache (default: true).</summary>
    public bool? EmbeddingUseCache { get; set; }
    /// <summary>Expected vector dimension (0 — do not validate).</summary>
    public int? EmbeddingDimensions { get; set; }

    /// <summary>Percentile of distances used for boundaries, (0, 1].</summary>
    public double? SemanticPercentile { get; set; }
    /// <summary>Minimum boundary distance.</summary>
    public double? SemanticMinDistance { get; set; }
    /// <summary>TextTiling window (1 — adjacent atoms).</summary>
    public int? SemanticWindow { get; set; }
    /// <summary>Input size limit in document mode, bytes.</summary>
    public long? SemanticMaxBytes { get; set; }
    /// <summary>Streaming window size, bytes.</summary>
    public int? SemanticStreamWindowBytes { get; set; }
    /// <summary>Overlap between streaming windows, bytes.</summary>
    public int? SemanticStreamOverlapBytes { get; set; }
    /// <summary>Behavior when the limit is exceeded: recursive | error | streaming.</summary>
    public string? SemanticFallback { get; set; }
    /// <summary>Merge atoms shorter than N characters.</summary>
    public int? SemanticMinAtomChars { get; set; }
    /// <summary>Pre-split atoms longer than N tokens.</summary>
    public int? SemanticMaxAtomTokens { get; set; }

    public ChunkRequest ToRequest(string text)
    {
        return new ChunkRequest
        {
            Text = text,
            Strategy = Strategy,
            Tokenizer = Tokenizer,
            TokenizerModel = TokenizerModel,
            TokenizerPath = TokenizerPath,
            TokenizerCacheDir = TokenizerCacheDir,
            Prefix = Prefix,
            MaxTokens = MaxTokens,
            MaxChars = MaxChars,
            MaxWords = MaxWords,
            MaxBytes = MaxBytes,
            MinTokens = MinTokens,
            MinChars = MinChars,
            MinWords = MinWords,
            OverlapTokens = OverlapTokens,
            OverlapChars = OverlapChars,
            Trim = Trim ?? true,
            DropSmall = DropSmall ?? false,
            Separators = ParseSeparators(Separators),
            Metadata = ParseMetadata(Metadata),
            Source = Source,
            Embedding = BuildEmbedding(),
            Semantic = BuildSemantic()
        };
    }

    private EmbeddingRequest BuildEmbedding()
    {
        var options = ParseOptions<EmbeddingRequest>(Embedding, "embedding") ?? new();
        return options with
        {
            Provider = EmbeddingProvider ?? options.Provider,
            Endpoint = EmbeddingEndpoint ?? options.Endpoint,
            Model = EmbeddingModel ?? options.Model,
            BatchSize = EmbeddingBatchSize ?? options.BatchSize,
            MaxBatchChars = EmbeddingMaxBatchChars ?? options.MaxBatchChars,
            Concurrency = EmbeddingConcurrency ?? options.Concurrency,
            TimeoutSeconds = EmbeddingTimeoutSeconds ?? options.TimeoutSeconds,
            MaxRetries = EmbeddingMaxRetries ?? options.MaxRetries,
            Prompt = EmbeddingPrompt ?? options.Prompt,
            CacheDir = EmbeddingCacheDir ?? options.CacheDir,
            UseCache = EmbeddingUseCache ?? options.UseCache,
            Dimensions = EmbeddingDimensions ?? options.Dimensions
        };
    }

    private SemanticRequest BuildSemantic()
    {
        var options = ParseOptions<SemanticRequest>(Semantic, "semantic") ?? new();
        return options with
        {
            Percentile = SemanticPercentile ?? options.Percentile,
            MinDistance = SemanticMinDistance ?? options.MinDistance,
            Window = SemanticWindow ?? options.Window,
            MaxBytes = SemanticMaxBytes ?? options.MaxBytes,
            StreamWindowBytes = SemanticStreamWindowBytes ?? options.StreamWindowBytes,
            StreamOverlapBytes = SemanticStreamOverlapBytes ?? options.StreamOverlapBytes,
            Fallback = SemanticFallback ?? options.Fallback,
            MinAtomChars = SemanticMinAtomChars ?? options.MinAtomChars,
            MaxAtomTokens = SemanticMaxAtomTokens ?? options.MaxAtomTokens
        };
    }

    private static T? ParseOptions<T>(string? value, string field) where T : class
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try
        {
            return JsonSerializer.Deserialize<T>(value, new JsonSerializerOptions(JsonSerializerDefaults.Web))
                ?? throw new ArgumentException($"The '{field}' field must be a JSON object, not null.");
        }
        catch (JsonException)
        {
            throw new ArgumentException($"The '{field}' field must be a valid JSON object.");
        }
    }

    private static string[]? ParseSeparators(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<string[]>(value);
        }
        catch (JsonException)
        {
            throw new ArgumentException("The 'separators' field must be a JSON array of strings, for example [\"\\n\\n\",\"\\n\"].");
        }
    }

    private static Dictionary<string, object?>? ParseMetadata(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<Dictionary<string, object?>>(value);
        }
        catch (JsonException)
        {
            throw new ArgumentException("The 'metadata' field must be a JSON object, for example {\"lang\":\"ru\"}.");
        }
    }
}
