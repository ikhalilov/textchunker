namespace TextChunker.Api.Contracts;

/// <summary>Запрос на нарезку текста. Все параметры, кроме <see cref="Text"/>, опциональны.</summary>
public sealed record ChunkRequest
{
    /// <summary>Входной текст (обязателен).</summary>
    public string? Text { get; init; }

    /// <summary>Стратегия: fixed | recursive | paragraph | semantic (по умолчанию recursive).</summary>
    public string? Strategy { get; init; }

    /// <summary>Токенизатор: none | char | word | regex | huggingface (по умолчанию word).</summary>
    public string? Tokenizer { get; init; }

    /// <summary>HF-модель или путь к tokenizer.json.</summary>
    public string? TokenizerModel { get; init; }

    /// <summary>Явный путь к локальному tokenizer.json (приоритетнее TokenizerModel).</summary>
    public string? TokenizerPath { get; init; }

    /// <summary>Каталог кэша tokenizer.json (переопределяет конфигурацию сервера).</summary>
    public string? TokenizerCacheDir { get; init; }

    public string? Prefix { get; init; }

    public int? MaxTokens { get; init; }
    public int? MaxChars { get; init; }
    public int? MaxWords { get; init; }
    public int? MaxBytes { get; init; }

    public int? MinTokens { get; init; }
    public int? MinChars { get; init; }
    public int? MinWords { get; init; }

    public int? OverlapTokens { get; init; }
    public int? OverlapChars { get; init; }

    public bool Trim { get; init; } = true;
    public bool DropSmall { get; init; }

    /// <summary>Разделители recursive (литеральные строки, по убыванию приоритета).</summary>
    public string[]? Separators { get; init; }

    /// <summary>Пользовательские метаданные (произвольный JSON-объект).</summary>
    public Dictionary<string, object?>? Metadata { get; init; }

    /// <summary>Метка источника; попадает в top-level `source` и `id` (по умолчанию "api").</summary>
    public string? Source { get; init; }

    /// <summary>Параметры эмбеддингов (только для strategy = "semantic").</summary>
    public EmbeddingRequest? Embedding { get; init; }

    /// <summary>Параметры семантических границ (только для strategy = "semantic").</summary>
    public SemanticRequest? Semantic { get; init; }
}
