namespace TextChunker.Tokenizers;

using System;
using System.Collections.Generic;

using TextChunker.Abstractions;

/// <summary>
/// Tokenizer factory: maps the <c>--tokenizer</c> name to an <see cref="ITokenizer"/> implementation.
/// By default (no name given) — the word tokenizer, with a CLI warning that it is approximate.
/// </summary>
public static class TokenizerFactory
{
    /// <summary>Names supported by the <c>--tokenizer</c> parameter.</summary>
    public static readonly IReadOnlyList<string> SupportedNames = new[]
    {
        "none", "char", "word", "regex", "huggingface"
    };

    /// <summary>
    /// Create a tokenizer by name. An unknown name yields <see cref="ArgumentOutOfRangeException"/>
    /// (the CLI maps it to exit code 2); a model loading error yields <see cref="TokenizerLoadException"/> (exit code 5).
    /// </summary>
    public static ITokenizer Create(string? name, string? model = null, string? cacheDir = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return CreateDefault();
        }

        string key = name.Trim().ToLowerInvariant();
        return key switch
        {
            "none" => new NullTokenizer(),
            "char" => new CharTokenizer(),
            "word" => new WordTokenizer(),
            "regex" => new RegexTokenizer(),
            "huggingface" => new HuggingFaceTokenizer(model, cacheDir),
            _ => throw new ArgumentOutOfRangeException(
                nameof(name),
                name,
                $"Unknown tokenizer '{name}'. Valid values: {string.Join(", ", SupportedNames)}.")
        };
    }

    /// <summary>Default tokenizer — word-based (approximate).</summary>
    public static ITokenizer CreateDefault() => new WordTokenizer();
}
