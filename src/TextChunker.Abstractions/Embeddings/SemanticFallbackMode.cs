namespace TextChunker.Abstractions.Embeddings;

/// <summary>Behavior when the size limit of the semantic mode is exceeded.</summary>
public enum SemanticFallbackMode
{
    /// <summary>Chunk via <c>recursive</c> with a warning on stderr.</summary>
    Recursive,

    /// <summary>Fail with an error (CLI exit 2 / API 413).</summary>
    Error,

    /// <summary>Process large documents in overlapping windows (bounded memory).</summary>
    Streaming
}
