namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// Параметры провайдера эмбеддингов. Значения по умолчанию соответствуют локальному
/// llama.cpp-серверу с <c>embeddinggemma-2</c> (см. docs/semantic-chunking.md).
/// </summary>
public sealed record EmbeddingOptions
{
    /// <summary>Провайдер: <c>llama-cpp</c> | <c>hash</c>.</summary>
    public string Provider { get; init; } = "llama-cpp";

    /// <summary>Базовый URL сервера эмбеддингов (используется только для <c>llama-cpp</c>).</summary>
    public string Endpoint { get; init; } = "http://127.0.0.1:9932";

    /// <summary>Имя модели в запросе; null/пусто — поле <c>model</c> не отправляется.</summary>
    public string? Model { get; init; }

    /// <summary>Ожидаемая размерность векторов (0 — не проверять).</summary>
    public int Dimensions { get; init; } = 768;

    /// <summary>Размер батча при обращении к серверу.</summary>
    public int BatchSize { get; init; } = 32;

    /// <summary>
    /// Символьный бюджет батча (консервативная оценка токенов): батч закрывается,
    /// когда суммарная длина текстов превышает лимит. 0 — ограничивать только BatchSize.
    /// </summary>
    public int MaxBatchChars { get; init; } = 8_000;

    /// <summary>
    /// Сколько батчей отправлять параллельно (1 — последовательно).
    /// Имеет смысл при сервере с несколькими слотами (<c>--parallel N</c>).
    /// </summary>
    public int Concurrency { get; init; } = 1;

    /// <summary>Префикс-промпт к каждому тексту (null — без промпта). Входит в ключ кэша.</summary>
    public string? Prompt { get; init; }

    /// <summary>Каталог кэша эмбеддингов; null — путь по умолчанию.</summary>
    public string? CacheDir { get; init; }

    /// <summary>false — отключить дисковый кэш.</summary>
    public bool UseCache { get; init; } = true;

    /// <summary>Максимальный размер кэша в байтах; null — без лимита.</summary>
    public long? CacheMaxBytes { get; init; }

    /// <summary>Удалять записи кэша старше N дней; null — без TTL.</summary>
    public int? CacheTtlDays { get; init; }

    /// <summary>Таймаут HTTP-запроса.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromSeconds(120);

    /// <summary>Число повторных попыток на сетевые ошибки и 429/5xx.</summary>
    public int MaxRetries { get; init; } = 3;

    /// <summary>
    /// API-ключ. Заполняется фабрикой из переменной окружения — из тела запроса
    /// (CLI/API) ключ никогда не принимается.
    /// </summary>
    public string? ApiKey { get; init; }

    /// <summary>Проверить согласованность параметров (невалидные значения → ArgumentException, exit 2 / 400).</summary>
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
