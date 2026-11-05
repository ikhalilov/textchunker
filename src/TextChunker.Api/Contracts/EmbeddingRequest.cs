namespace TextChunker.Api.Contracts;

/// <summary>
/// Параметры эмбеддингов для <c>strategy = "semantic"</c>. API-ключ здесь не принимается:
/// он читается только из переменной окружения на сервере.
/// </summary>
public sealed record EmbeddingRequest
{
    /// <summary>llama-cpp | hash (по умолчанию llama-cpp).</summary>
    public string? Provider { get; init; }

    /// <summary>URL сервера эмбеддингов (по умолчанию http://127.0.0.1:9932).</summary>
    public string? Endpoint { get; init; }

    /// <summary>Имя модели в запросе к серверу эмбеддингов.</summary>
    public string? Model { get; init; }

    /// <summary>Размер батча эмбеддингов (по умолчанию 32).</summary>
    public int? BatchSize { get; init; }

    /// <summary>Символьный бюджет батча (консервативная оценка токенов; 0 — без ограничения).</summary>
    public int? MaxBatchChars { get; init; }

    /// <summary>Сколько батчей отправлять параллельно (1 — последовательно).</summary>
    public int? Concurrency { get; init; }

    /// <summary>Таймаут HTTP-запроса к серверу, секунды (по умолчанию 120).</summary>
    public int? TimeoutSeconds { get; init; }

    /// <summary>Повторы на сетевые ошибки и 429/5xx (по умолчанию 3).</summary>
    public int? MaxRetries { get; init; }

    /// <summary>Префикс-промпт, добавляемый к каждому атому перед эмбеддингом.</summary>
    public string? Prompt { get; init; }

    /// <summary>Каталог кэша эмбеддингов (по умолчанию LocalApplicationData/TextChunker/embeddings).</summary>
    public string? CacheDir { get; init; }

    /// <summary>Использовать дисковый кэш эмбеддингов (по умолчанию true).</summary>
    public bool? UseCache { get; init; }

    /// <summary>Ожидаемая размерность векторов (по умолчанию 768; 0 — не проверять).</summary>
    public int? Dimensions { get; init; }
}

/// <summary>Параметры семантической нарезки.</summary>
public sealed record SemanticRequest
{
    /// <summary>Перцентиль расстояний 1 − cos для границ, (0, 1] (по умолчанию 0.90).</summary>
    public double? Percentile { get; init; }

    /// <summary>Минимальная дистанция границы (по умолчанию 0.02).</summary>
    public double? MinDistance { get; init; }

    /// <summary>Окно TextTiling: 1 — соседние атомы (по умолчанию 1).</summary>
    public int? Window { get; init; }

    /// <summary>Лимит объёма входа в document-mode, байт (по умолчанию 20000000).</summary>
    public long? MaxBytes { get; init; }

    /// <summary>Размер окна streaming-режима в байтах (по умолчанию 4000000).</summary>
    public int? StreamWindowBytes { get; init; }

    /// <summary>Перекрытие соседних streaming-окон в байтах (по умолчанию 256000).</summary>
    public int? StreamOverlapBytes { get; init; }

    /// <summary>
    /// Поведение при превышении <c>maxBytes</c>: <c>recursive</c> — откат, <c>error</c> — 413,
    /// <c>streaming</c> — оконная обработка (по умолчанию recursive).
    /// </summary>
    public string? Fallback { get; init; }

    /// <summary>Склейка атомов короче N символов (по умолчанию 20).</summary>
    public int? MinAtomChars { get; init; }

    /// <summary>Предварительное дробление атомов длиннее N токенов (по умолчанию 512).</summary>
    public int? MaxAtomTokens { get; init; }
}
