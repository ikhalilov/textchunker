using TextChunker.Abstractions;
using TextChunker.Chunking.Semantic;

namespace TextChunker.Chunking;

/// <summary>Фабрика стратегий нарезки по имени <c>--strategy</c>.</summary>
public static class ChunkerFactory
{
    public static readonly IReadOnlyList<string> SupportedStrategies = new[]
    {
        "fixed", "recursive", "paragraph", "semantic"
    };

    /// <summary>Стратегия, требующая внешних эмбеддингов (document-mode).</summary>
    public const string SemanticStrategyName = "semantic";

    /// <summary>Создать стратегию; неизвестное имя — <see cref="ArgumentOutOfRangeException"/> (exit 2).</summary>
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
                $"Неизвестная стратегия '{strategy}'. Допустимые значения: {string.Join(", ", SupportedStrategies)}.")
        };
    }

    /// <summary>true — стратегия требует семантического плана (эмбеддингов).</summary>
    public static bool RequiresEmbeddings(string? strategy)
    {
        string key = string.IsNullOrWhiteSpace(strategy) ? "recursive" : strategy.Trim().ToLowerInvariant();
        return key == SemanticStrategyName;
    }
}
