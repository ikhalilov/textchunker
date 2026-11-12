namespace TextChunker.Abstractions;

/// <summary>
/// A chunk is a fragment of the source text produced by chunking. The id/seq/source fields are
/// required; sizes are computed during assembly. Offsets are relative to the source text (or to the
/// normalized text when `--normalize` is enabled).
/// </summary>
public sealed record Chunk
{
    /// <summary>Deterministic identifier: `"{source}:{seq:D6}"` by default.</summary>
    public required string Id { get; init; }

    /// <summary>Sequential number of the chunk, starting at 1.</summary>
    public required int Seq { get; init; }

    /// <summary>Source name (file path or "stdin").</summary>
    public required string Source { get; init; }

    public required string Text { get; init; }

    /// <summary>Start CharIndex (inclusive) in the source/normalized text.</summary>
    public long? StartChar { get; init; }

    /// <summary>End CharIndex (exclusive) in the source/normalized text.</summary>
    public long? EndChar { get; init; }

    /// <summary>Character count (Unicode code units / graphemes). Set from Text.Length.</summary>
    public int? CharCount { get; init; }

    /// <summary>Number of bytes in the UTF-8 text.</summary>
    public int? ByteCount { get; init; }

    /// <summary>Exact token count from the model tokenizer (including the prefix) when one is configured.</summary>
    public int? TokenCount { get; init; }

    /// <summary>Metadata: strategy, tokenizer, model, prefix/prefixTokens, normalized, user metadata.</summary>
    public required IReadOnlyDictionary<string, object?> Metadata { get; init; }
}
