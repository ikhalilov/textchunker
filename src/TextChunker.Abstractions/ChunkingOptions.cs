namespace TextChunker.Abstractions;

/// <summary>Chunking settings. A single unit of measure is active, determined by the max-* parameter.</summary>
public sealed record ChunkingOptions
{
    /// <summary>`fixed`, `recursive`, `paragraph` (MVP).</summary>
    public string Strategy { get; init; } = "recursive";

    public int? MaxTokens { get; init; }
    public int? MaxChars { get; init; }
    public int? MaxWords { get; init; }
    /// <summary>Optional unit: bytes (network/binary limits).</summary>
    public int? MaxBytes { get; init; }

    public int? MinTokens { get; init; }
    public int? MinChars { get; init; }
    public int? MinWords { get; init; }

    public int? OverlapTokens { get; init; }
    public int? OverlapChars { get; init; }

    /// <summary>Trim whitespace at chunk edges (enabled by default).</summary>
    public bool Trim { get; init; } = true;

    /// <summary>Drop the final chunk if it is smaller than min, without merging it into the previous one.</summary>
    public bool DropSmall { get; init; }

    /// <summary>Task prefix counted toward the limit: tokenCount(prefix + chunk) &lt;= max-tokens.</summary>
    public string? Prefix { get; init; }

    /// <summary>Hierarchical `recursive` separators, from coarse to fine. An empty string means a hard split.</summary>
    public IReadOnlyList<string> Separators { get; init; } = DefaultSeparators;

    public static readonly IReadOnlyList<string> DefaultSeparators = [
        "\n\n", "\n", ". ", "! ", "? ", "; ", ", ", " ", ""
    ];

    public SizeUnit Unit { get; }
    public int? MaxSize { get; }

    /// <summary>Active unit of measure derived from max-* (priority: tokens > chars > words > bytes).</summary>
    static SizeUnit DetermineUnit(ChunkingOptions o) =>
        o.MaxTokens is { } t ? SizeUnit.Tokens :
        o.MaxChars is { } c ? SizeUnit.Chars :
        o.MaxWords is { } w ? SizeUnit.Words :
        o.MaxBytes is { } b ? SizeUnit.Bytes :
        SizeUnit.Tokens; // default for recursive/paragraph without an explicit max: tokens (fallback: word/char)

    public ChunkingOptions(
        string? strategy = null,
        int? maxTokens = null,
        int? maxChars = null,
        int? maxWords = null,
        int? maxBytes = null,
        int? minTokens = null,
        int? minChars = null,
        int? minWords = null,
        int? overlapTokens = null,
        int? overlapChars = null,
        bool trim = true,
        bool dropSmall = false,
        string? prefix = null,
        IReadOnlyList<string>? separators = null)
    {
        Strategy = strategy ?? "recursive";
        MaxTokens = maxTokens;
        MaxChars = maxChars;
        MaxWords = maxWords;
        MaxBytes = maxBytes;
        MinTokens = minTokens;
        MinChars = minChars;
        MinWords = minWords;
        OverlapTokens = overlapTokens;
        OverlapChars = overlapChars;
        Trim = trim;
        DropSmall = dropSmall;
        Prefix = prefix;
        Separators = separators is { } s && s.Any() ? s : DefaultSeparators;
        Unit = DetermineUnit(this);
        MaxSize = Unit switch
        {
            SizeUnit.Tokens => MaxTokens,
            SizeUnit.Chars => MaxChars,
            SizeUnit.Words => MaxWords,
            SizeUnit.Bytes => MaxBytes,
            _ => null
        };
    }
}

public enum SizeUnit
{
    Tokens,
    Chars,
    Words,
    Bytes
}
