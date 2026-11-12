using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>Validation of chunking parameters (invalid values → ArgumentException, CLI: exit 2).</summary>
public static class ChunkingOptionsValidator
{
    public static void Validate(ChunkingOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        RequirePositive(options.MaxTokens, "--max-tokens");
        RequirePositive(options.MaxChars, "--max-chars");
        RequirePositive(options.MaxWords, "--max-words");
        RequirePositive(options.MaxBytes, "--max-bytes");

        RequireNonNegative(options.MinTokens, "--min-tokens");
        RequireNonNegative(options.MinChars, "--min-chars");
        RequireNonNegative(options.MinWords, "--min-words");

        RequireNonNegative(options.OverlapTokens, "--overlap-tokens");
        RequireNonNegative(options.OverlapChars, "--overlap-chars");

        ValidateMinMax(options.MinTokens, options.MaxTokens, "--min-tokens", "--max-tokens");
        ValidateMinMax(options.MinChars, options.MaxChars, "--min-chars", "--max-chars");
        ValidateMinMax(options.MinWords, options.MaxWords, "--min-words", "--max-words");

        ValidateLessThanMax(options.OverlapTokens, options.MaxTokens, "--overlap-tokens", "--max-tokens");
        ValidateLessThanMax(options.OverlapChars, options.MaxChars, "--overlap-chars", "--max-chars");

        if (options.Separators is null || options.Separators.Count == 0)
        {
            throw new ArgumentException("The separator list cannot be empty (--separators).", nameof(options));
        }
    }

    private static void RequirePositive(int? value, string name)
    {
        if (value is <= 0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} must be greater than zero.");
        }
    }

    private static void RequireNonNegative(int? value, string name)
    {
        if (value is < 0)
        {
            throw new ArgumentOutOfRangeException(name, value, $"{name} cannot be negative.");
        }
    }

    private static void ValidateMinMax(int? smaller, int? larger, string smallerName, string largerName)
    {
        if (smaller is { } a && larger is { } b && a > b)
        {
            throw new ArgumentOutOfRangeException(
                smallerName,
                a,
                $"{smallerName} ({a}) cannot be greater than {largerName} ({b}).");
        }
    }

    private static void ValidateLessThanMax(int? value, int? max, string valueName, string maxName)
    {
        if (value is { } a && max is { } b && a >= b)
        {
            throw new ArgumentOutOfRangeException(
                valueName,
                a,
                $"{valueName} ({a}) must be less than {maxName} ({b}).");
        }
    }
}
