namespace TextChunker.Abstractions.Embeddings;

/// <summary>Режим поведения при превышении лимита объёма семантического режима.</summary>
public enum SemanticFallbackMode
{
    /// <summary>Нарезать через <c>recursive</c> с предупреждением в stderr.</summary>
    Recursive,

    /// <summary>Завершиться с ошибкой (CLI exit 2 / API 413).</summary>
    Error,

    /// <summary>Обрабатывать большие документы окнами с перекрытием (bounded memory).</summary>
    Streaming
}
