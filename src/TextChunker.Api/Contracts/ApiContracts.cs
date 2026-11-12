namespace TextChunker.Api.Contracts;

/// <summary>Text chunking request. All parameters except <see cref="Text"/> are optional.</summary>
public sealed record ChunkRequest
{
    /// <summary>Input text (required).</summary>
    public string? Text { get; init; }

    /// <summary>Strategy: fixed | recursive | paragraph | semantic (default: recursive).</summary>
    public string? Strategy { get; init; }

    /// <summary>Tokenizer: none | char | word | regex | huggingface (default: word).</summary>
    public string? Tokenizer { get; init; }

    /// <summary>HuggingFace model or path to tokenizer.json.</summary>
    public string? TokenizerModel { get; init; }

    /// <summary>Explicit path to a local tokenizer.json (takes precedence over TokenizerModel).</summary>
    public string? TokenizerPath { get; init; }

    /// <summary>tokenizer.json cache directory (overrides the server configuration).</summary>
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

    /// <summary>Recursive separators (literal strings, in descending priority order).</summary>
    public string[]? Separators { get; init; }

    /// <summary>User metadata (arbitrary JSON object).</summary>
    public Dictionary<string, object?>? Metadata { get; init; }

    /// <summary>Source label; lands in the top-level `source` and `id` (default: "api").</summary>
    public string? Source { get; init; }

    /// <summary>Embedding parameters (only for strategy = "semantic").</summary>
    public EmbeddingRequest? Embedding { get; init; }

    /// <summary>Semantic boundary parameters (only for strategy = "semantic").</summary>
    public SemanticRequest? Semantic { get; init; }
}
