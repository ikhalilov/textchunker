using Microsoft.Extensions.Options;

using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;
using TextChunker.Api.Contracts;
using TextChunker.Chunking;
using TextChunker.Chunking.Semantic;
using TextChunker.Embeddings;

namespace TextChunker.Api.Services;

/// <summary>
/// Assembles the chunking pipeline for an HTTP request: validation, tokenizer (with cache),
/// strategy, in-memory source, and service. Domain errors are translated into HTTP codes
/// via <see cref="ApiErrors"/>.
/// </summary>
public sealed class ChunkingEngine(
    TokenizerCache tokenizers,
    IOptionsMonitor<ApiOptions> options)
{
    private readonly TokenizerCache _tokenizers = tokenizers;
    private readonly IOptionsMonitor<ApiOptions> _options = options;

    /// <summary>Creates a chunking session for an HTTP request.</summary>
    /// <param name="request">The request parameters.</param>
    /// <returns>A session with the chunking service and the in-memory source.</returns>
    public ChunkSession CreateSession(ChunkRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        ApiOptions apiOptions = _options.CurrentValue;

        if (string.IsNullOrWhiteSpace(request.Text))
        {
            throw new ArgumentException("Поле 'text' обязательно и не может быть пустым.");
        }
        if (request.Text.Length > apiOptions.MaxTextChars)
        {
            throw new PayloadTooLargeException(
                $"Текст длиннее лимита {apiOptions.MaxTextChars} символов.");
        }

        ChunkingOptions options = BuildOptions(request);
        ChunkingOptionsValidator.Validate(options);

        string? cacheDir = string.IsNullOrWhiteSpace(request.TokenizerCacheDir)
            ? apiOptions.TokenizerCacheDir
            : request.TokenizerCacheDir;

        ITokenizer tokenizer = _tokenizers.Get(
            request.Tokenizer,
            request.TokenizerPath ?? request.TokenizerModel,
            cacheDir);

        IChunker chunker = ChunkerFactory.Create(request.Strategy);

        string sourceName = string.IsNullOrWhiteSpace(request.Source) ? "api" : request.Source!;
        var source = new InMemoryTextSource(request.Text, sourceName);

        var metadata = new Dictionary<string, object?>();
        if (request.Metadata is not null)
        {
            foreach (KeyValuePair<string, object?> pair in request.Metadata)
            {
                metadata[pair.Key] = pair.Value;
            }
        }
        if (!string.IsNullOrWhiteSpace(request.Source))
        {
            metadata["source"] = request.Source;
        }

        var service = new ChunkingService(
            tokenizer,
            chunker,
            options,
            userMetadata: metadata,
            semanticPlanner: CreatePlanner(request),
            semanticOptions: BuildSemanticOptions(request));

        return new ChunkSession(service, source);
    }

    /// <summary>Chunks according to the request parameters.</summary>
    /// <param name="request">The request parameters.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chunks and aggregated statistics.</returns>
    public async Task<ChunkResult> RunAsync(ChunkRequest request, CancellationToken cancellationToken)
    {
        ChunkSession session = CreateSession(request);
        return await RunAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Chunking over an already created session (needed by the streaming endpoint).</summary>
    /// <param name="session">The chunking session.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The chunks and aggregated statistics.</returns>
    public async Task<ChunkResult> RunAsync(ChunkSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writer = new CollectingChunkWriter();
        ChunkingStats stats = await session.Service
            .ProcessAsync(session.Source, writer, cancellationToken)
            .ConfigureAwait(false);
        return new ChunkResult(writer.Chunks, stats);
    }

    /// <summary>The boundary planner — only for strategy = "semantic".</summary>
    private static ISemanticPlanner? CreatePlanner(ChunkRequest request)
    {
        if (!ChunkerFactory.RequiresEmbeddings(request.Strategy))
        {
            return null;
        }

        EmbeddingOptions embedding = BuildEmbeddingOptions(request.Embedding);
        IEmbeddingProvider provider = EmbeddingProviderFactory.Create(embedding);
        return new SemanticPlanner(provider, BuildSemanticOptions(request));
    }

    private static EmbeddingOptions BuildEmbeddingOptions(EmbeddingRequest? request)
    {
        var defaults = new EmbeddingOptions();
        var options = new EmbeddingOptions
        {
            Provider = string.IsNullOrWhiteSpace(request?.Provider) ? defaults.Provider : request!.Provider!.Trim(),
            Endpoint = string.IsNullOrWhiteSpace(request?.Endpoint) ? defaults.Endpoint : request!.Endpoint!.Trim(),
            Model = string.IsNullOrWhiteSpace(request?.Model) ? null : request!.Model!.Trim(),
            BatchSize = request?.BatchSize ?? defaults.BatchSize,
            MaxBatchChars = request?.MaxBatchChars ?? defaults.MaxBatchChars,
            Concurrency = request?.Concurrency ?? defaults.Concurrency,
            Timeout = request?.TimeoutSeconds is { } seconds
                ? TimeSpan.FromSeconds(seconds)
                : defaults.Timeout,
            MaxRetries = request?.MaxRetries ?? defaults.MaxRetries,
            Prompt = string.IsNullOrWhiteSpace(request?.Prompt) ? null : request!.Prompt,
            CacheDir = string.IsNullOrWhiteSpace(request?.CacheDir) ? null : request!.CacheDir!.Trim(),
            UseCache = request?.UseCache ?? defaults.UseCache,
            Dimensions = request?.Dimensions ?? defaults.Dimensions
        };
        options.Validate();
        return options;
    }

    private static SemanticOptions BuildSemanticOptions(ChunkRequest request)
    {
        var defaults = new SemanticOptions();
        var options = new SemanticOptions
        {
            Percentile = request.Semantic?.Percentile ?? defaults.Percentile,
            MinDistance = request.Semantic?.MinDistance ?? defaults.MinDistance,
            Window = request.Semantic?.Window ?? defaults.Window,
            MaxBytes = request.Semantic?.MaxBytes ?? defaults.MaxBytes,
            StreamWindowBytes = request.Semantic?.StreamWindowBytes ?? defaults.StreamWindowBytes,
            StreamOverlapBytes = request.Semantic?.StreamOverlapBytes ?? defaults.StreamOverlapBytes,
            Fallback = SemanticOptions.ParseFallback(request.Semantic?.Fallback),
            MinAtomChars = request.Semantic?.MinAtomChars ?? defaults.MinAtomChars,
            MaxAtomTokens = request.Semantic?.MaxAtomTokens ?? defaults.MaxAtomTokens
        };
        options.Validate();
        return options;
    }

    private static ChunkingOptions BuildOptions(ChunkRequest request)
    {
        int? maxTokens = request.MaxTokens;
        if (maxTokens is null && request.MaxChars is null && request.MaxWords is null && request.MaxBytes is null)
        {
            maxTokens = 512; // same default as in the CLI
        }

        return new ChunkingOptions(
            strategy: request.Strategy,
            maxTokens: maxTokens,
            maxChars: request.MaxChars,
            maxWords: request.MaxWords,
            maxBytes: request.MaxBytes,
            minTokens: request.MinTokens,
            minChars: request.MinChars,
            minWords: request.MinWords,
            overlapTokens: request.OverlapTokens,
            overlapChars: request.OverlapChars,
            trim: request.Trim,
            dropSmall: request.DropSmall,
            prefix: request.Prefix,
            separators: request.Separators);
    }
}
