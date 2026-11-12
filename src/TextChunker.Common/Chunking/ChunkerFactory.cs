using TextChunker.Abstractions;
using TextChunker.Chunking.Semantic;

namespace TextChunker.Chunking;

/// <summary>Factory of chunking strategies by the <c>--strategy</c> name.</summary>
public static class ChunkerFactory
{
    public static readonly IReadOnlyList<string> SupportedStrategies = new[]
    {
        "fixed", "recursive", "paragraph", "semantic"
    };

    /// <summary>Strategy that requires external embeddings (document mode).</summary>
    public const string SemanticStrategyName = "semantic";

    /// <summary>Create a strategy; an unknown name yields <see cref="ArgumentOutOfRangeException"/> (exit 2).</summary>
    public static IChunker Create(string? strategy)
    {
        string key = string.IsNullOrWhiteSpace(strategy)
            ? "recursive"
            : strategy.Trim().ToLowerInvariant();

        return key switch
        {
            "fixed" => new FixedChunker(),
            "recursive" => new RecursiveChunker(),
            "paragraph" => new ParagraphChunker(),
            SemanticStrategyName => new SemanticChunker(),
            _ => throw new ArgumentOutOfRangeException(
                nameof(strategy),
                strategy,
                $"Unknown strategy '{strategy}'. Valid values: {string.Join(", ", SupportedStrategies)}.")
        };
    }

    /// <summary>true — the strategy requires a semantic plan (embeddings).</summary>
    public static bool RequiresEmbeddings(string? strategy)
    {
        string key = string.IsNullOrWhiteSpace(strategy) ? "recursive" : strategy.Trim().ToLowerInvariant();
        return key == SemanticStrategyName;
    }
}
