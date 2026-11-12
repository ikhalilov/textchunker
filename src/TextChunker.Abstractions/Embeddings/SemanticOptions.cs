namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// Semantic chunking settings. The defaults are fixed in
/// openspec/changes/chunker-semantic/design.md (decision 5).
/// </summary>
public sealed record SemanticOptions
{
    /// <summary>Percentile of boundary distances (0..1]: boundaries are placed where distance >= percentile.</summary>
    public double Percentile { get; init; } = 0.90;

    /// <summary>Minimum distance below which no boundary is placed (noise protection).</summary>
    public double MinDistance { get; init; } = 0.02;

    /// <summary>TextTiling window size (1 — adjacent atoms without a window).</summary>
    public int Window { get; init; } = 1;

    /// <summary>Maximum input size for the semantic mode, in bytes.</summary>
    public long MaxBytes { get; init; } = 20_000_000;

    /// <summary>Behavior when <see cref="MaxBytes"/> is exceeded.</summary>
    public SemanticFallbackMode Fallback { get; init; } = SemanticFallbackMode.Recursive;

    /// <summary>Atoms shorter than this number of characters are merged with their neighbors.</summary>
    public int MinAtomChars { get; init; } = 20;

    /// <summary>Atoms longer than this number of tokens are split beforehand.</summary>
    public int MaxAtomTokens { get; init; } = 512;

    /// <summary>Streaming window size in UTF-8 bytes (4 MB by default).</summary>
    public int StreamWindowBytes { get; init; } = 4_000_000;

    /// <summary>Overlap of adjacent streaming windows in UTF-8 bytes.</summary>
    public int StreamOverlapBytes { get; init; } = 256_000;

    /// <summary>Validate parameter consistency (invalid values → ArgumentException, exit 2 / 400).</summary>
    public void Validate()
    {
        if (Percentile <= 0 || Percentile > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Percentile),
                Percentile,
                "--semantic-percentile must be in the range (0, 1].");
        }

        if (MinDistance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinDistance),
                MinDistance,
                "--semantic-min-distance cannot be negative.");
        }

        if (Window < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Window), Window, "--semantic-window must be at least 1.");
        }

        if (MaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBytes), MaxBytes, "--semantic-max-bytes must be greater than zero.");
        }

        if (MinAtomChars < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinAtomChars), MinAtomChars, "--semantic-min-atom-chars cannot be negative.");
        }

        if (MaxAtomTokens < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAtomTokens), MaxAtomTokens, "--semantic-max-atom-tokens must be at least 1.");
        }

        if (StreamWindowBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StreamWindowBytes),
                StreamWindowBytes,
                "--semantic-stream-window-bytes must be greater than zero.");
        }

        if (StreamOverlapBytes < 0 || StreamOverlapBytes >= StreamWindowBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StreamOverlapBytes),
                StreamOverlapBytes,
                "--semantic-stream-overlap-bytes must be in the range [0, window).");
        }
    }

    /// <summary>Parses the <c>--semantic-fallback</c> value.</summary>
    public static SemanticFallbackMode ParseFallback(string? value)
    {
        string key = string.IsNullOrWhiteSpace(value) ? "recursive" : value.Trim().ToLowerInvariant();
        return key switch
        {
            "recursive" => SemanticFallbackMode.Recursive,
            "error" => SemanticFallbackMode.Error,
            "streaming" => SemanticFallbackMode.Streaming,
            _ => throw new ArgumentOutOfRangeException(
                nameof(value),
                value,
                $"Unknown --semantic-fallback mode '{value}'. Allowed values: recursive, error, streaming.")
        };
    }
}
