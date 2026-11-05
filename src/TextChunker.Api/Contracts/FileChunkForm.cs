using System.Text.Json;

namespace TextChunker.Api.Contracts;

/// <summary>
/// Поля формы multipart для загрузки файла (<c>POST /api/chunk/file</c>).
/// Совпадают по смыслу с <see cref="ChunkRequest"/>, но приходят как form-data.
/// </summary>
public sealed class FileChunkForm
{
    /// <summary>Стратегия: fixed | recursive | paragraph | semantic (по умолчанию recursive).</summary>
    public string? Strategy { get; set; }
    /// <summary>Токенизатор: none | char | word | regex | huggingface (по умолчанию word).</summary>
    public string? Tokenizer { get; set; }
    /// <summary>HF-модель или путь к tokenizer.json.</summary>
    public string? TokenizerModel { get; set; }
    /// <summary>Явный путь к локальному tokenizer.json (приоритетнее TokenizerModel).</summary>
    public string? TokenizerPath { get; set; }
    /// <summary>Каталог кэша tokenizer.json.</summary>
    public string? TokenizerCacheDir { get; set; }
    /// <summary>Task-префикс, учитываемый в лимите токенов.</summary>
    public string? Prefix { get; set; }

    /// <summary>Максимум токенов на чанк (по умолчанию 512, если не задан ни один лимит).</summary>
    public int? MaxTokens { get; set; }
    /// <summary>Максимум символов на чанк.</summary>
    public int? MaxChars { get; set; }
    /// <summary>Максимум слов на чанк.</summary>
    public int? MaxWords { get; set; }
    /// <summary>Максимум байт UTF-8 на чанк.</summary>
    public int? MaxBytes { get; set; }

    /// <summary>Минимум токенов для финального чанка.</summary>
    public int? MinTokens { get; set; }
    /// <summary>Минимум символов для финального чанка.</summary>
    public int? MinChars { get; set; }
    /// <summary>Минимум слов для финального чанка.</summary>
    public int? MinWords { get; set; }

    /// <summary>Перекрытие соседних чанков в токенах.</summary>
    public int? OverlapTokens { get; set; }
    /// <summary>Перекрытие соседних чанков в символах.</summary>
    public int? OverlapChars { get; set; }

    /// <summary>Обрезать пробелы по краям чанка (по умолчанию true).</summary>
    public bool? Trim { get; set; }
    /// <summary>Отбросить финальный чанк меньше минимума.</summary>
    public bool? DropSmall { get; set; }

    /// <summary>JSON-массив строк, например <c>["\n\n","\n"]</c>.</summary>
    public string? Separators { get; set; }

    /// <summary>JSON-объект пользовательских метаданных.</summary>
    public string? Metadata { get; set; }

    /// <summary>Метка источника для top-level source и id (по умолчанию имя файла).</summary>
    public string? Source { get; set; }

    /// <summary>Кодировка файла (по умолчанию utf-8; поддерживаются кодовые страницы).</summary>
    public string? Encoding { get; set; }

    /// <summary>replace (по умолчанию) или error.</summary>
    public string? InvalidEncoding { get; set; }

    /// <summary>JSON-объекты с теми же полями, что в JSON endpoint.</summary>
    public string? Embedding { get; set; }
    /// <summary>JSON-объект параметров семантических границ (как поле semantic в /api/chunk).</summary>
    public string? Semantic { get; set; }

    /// <summary>Провайдер эмбеддингов: llama-cpp | hash.</summary>
    public string? EmbeddingProvider { get; set; }
    /// <summary>URL сервера эмбеддингов.</summary>
    public string? EmbeddingEndpoint { get; set; }
    /// <summary>Имя модели в запросе к серверу эмбеддингов.</summary>
    public string? EmbeddingModel { get; set; }
    /// <summary>Размер батча эмбеддингов.</summary>
    public int? EmbeddingBatchSize { get; set; }
    /// <summary>Символьный бюджет батча (0 — только по количеству).</summary>
    public int? EmbeddingMaxBatchChars { get; set; }
    /// <summary>Сколько батчей отправлять параллельно.</summary>
    public int? EmbeddingConcurrency { get; set; }
    /// <summary>Таймаут HTTP-запроса к серверу эмбеддингов, секунды.</summary>
    public int? EmbeddingTimeoutSeconds { get; set; }
    /// <summary>Повторы на сетевые ошибки и 429/5xx.</summary>
    public int? EmbeddingMaxRetries { get; set; }
    /// <summary>Префикс-промпт, добавляемый к каждому атому.</summary>
    public string? EmbeddingPrompt { get; set; }
    /// <summary>Каталог кэша эмбеддингов.</summary>
    public string? EmbeddingCacheDir { get; set; }
    /// <summary>Использовать дисковый кэш эмбеддингов (по умолчанию true).</summary>
    public bool? EmbeddingUseCache { get; set; }
    /// <summary>Ожидаемая размерность векторов (0 — не проверять).</summary>
    public int? EmbeddingDimensions { get; set; }

    /// <summary>Перцентиль расстояний для границ, (0, 1].</summary>
    public double? SemanticPercentile { get; set; }
    /// <summary>Минимальная дистанция границы.</summary>
    public double? SemanticMinDistance { get; set; }
    /// <summary>Окно TextTiling (1 — соседние атомы).</summary>
    public int? SemanticWindow { get; set; }
    /// <summary>Лимит объёма входа document-mode, байт.</summary>
    public long? SemanticMaxBytes { get; set; }
    /// <summary>Размер окна streaming-режима, байт.</summary>
    public int? SemanticStreamWindowBytes { get; set; }
    /// <summary>Перекрытие streaming-окон, байт.</summary>
    public int? SemanticStreamOverlapBytes { get; set; }
    /// <summary>Поведение при превышении лимита: recursive | error | streaming.</summary>
    public string? SemanticFallback { get; set; }
    /// <summary>Склейка атомов короче N символов.</summary>
    public int? SemanticMinAtomChars { get; set; }
    /// <summary>Предварительное дробление атомов длиннее N токенов.</summary>
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
                ?? throw new ArgumentException($"Поле '{field}' должно быть JSON-объектом, а не null.");
        }
        catch (JsonException)
        {
            throw new ArgumentException($"Поле '{field}' должно быть корректным JSON-объектом.");
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
            throw new ArgumentException("Поле 'separators' должно быть JSON-массивом строк, например [\"\\n\\n\",\"\\n\"].");
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
            throw new ArgumentException("Поле 'metadata' должно быть JSON-объектом, например {\"lang\":\"ru\"}.");
        }
    }
}
