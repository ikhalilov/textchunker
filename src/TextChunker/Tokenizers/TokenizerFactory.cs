namespace TextChunker.Tokenizers;

using System;
using System.Collections.Generic;

using TextChunker.Abstractions;

/// <summary>
/// Фабрика токенизаторов: сопоставляет имя из <c>--tokenizer</c> реализации <see cref="ITokenizer"/>.
/// По умолчанию (имя не задано) — word-токенизатор с предупреждением в CLI о приблизительности.
/// </summary>
public static class TokenizerFactory
{
    /// <summary>Имена, поддерживаемые параметром <c>--tokenizer</c>.</summary>
    public static readonly IReadOnlyList<string> SupportedNames = new[]
    {
        "none", "char", "word", "regex", "huggingface"
    };

    /// <summary>
    /// Создать токенизатор по имени. Неизвестное имя — <see cref="ArgumentOutOfRangeException"/>
    /// (CLI переводит в exit code 2); ошибка загрузки модели — <see cref="TokenizerLoadException"/> (exit code 5).
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
                $"Неизвестный токенизатор '{name}'. Допустимые значения: {string.Join(", ", SupportedNames)}.")
        };
    }

    /// <summary>Токенизатор по умолчанию — word-based (приближённый).</summary>
    public static ITokenizer CreateDefault() => new WordTokenizer();
}
