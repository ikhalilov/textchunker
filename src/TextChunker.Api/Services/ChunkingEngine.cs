using Microsoft.Extensions.Options;

using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;
using TextChunker.Api.Contracts;
using TextChunker.Chunking;
using TextChunker.Chunking.Semantic;
using TextChunker.Embeddings;

namespace TextChunker.Api.Services;

/// <summary>
/// Собирает конвейер нарезки для HTTP-запроса: валидация, токенизатор (с кэшем),
/// стратегия, in-memory источник и сервис. Ошибки домена транслируются в HTTP-коды
/// через <see cref="ApiErrors"/>.
/// </summary>
public sealed class ChunkingEngine(
    TokenizerCache tokenizers,
    IOptionsMonitor<ApiOptions> options)
{
    private readonly TokenizerCache _tokenizers = tokenizers;
    private readonly IOptionsMonitor<ApiOptions> _options = options;

    /// <summary>Создаёт сессию нарезки для HTTP-запроса.</summary>
    /// <param name="request">Параметры запроса.</param>
    /// <returns>Сессия с сервисом нарезки и in-memory источником.</returns>
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

    /// <summary>Выполняет нарезку по параметрам запроса.</summary>
    /// <param name="request">Параметры запроса.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Чанки и агрегированная статистика.</returns>
    public async Task<ChunkResult> RunAsync(ChunkRequest request, CancellationToken cancellationToken)
    {
        ChunkSession session = CreateSession(request);
        return await RunAsync(session, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Нарезка по уже созданной сессии (нужно streaming-эндпоинту).</summary>
    /// <param name="session">Сессия нарезки.</param>
    /// <param name="cancellationToken">Токен отмены.</param>
    /// <returns>Чанки и агрегированная статистика.</returns>
    public async Task<ChunkResult> RunAsync(ChunkSession session, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(session);

        var writer = new CollectingChunkWriter();
        ChunkingStats stats = await session.Service
            .ProcessAsync(session.Source, writer, cancellationToken)
            .ConfigureAwait(false);
        return new ChunkResult(writer.Chunks, stats);
    }

    /// <summary>Планировщик границ — только для strategy = "semantic".</summary>
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
            maxTokens = 512; // тот же дефолт, что и в CLI
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
