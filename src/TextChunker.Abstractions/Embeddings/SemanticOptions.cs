namespace TextChunker.Abstractions.Embeddings;

/// <summary>
/// Параметры семантической нарезки. Значения по умолчанию зафиксированы в
/// openspec/changes/chunker-semantic/design.md (решение 5).
/// </summary>
public sealed record SemanticOptions
{
    /// <summary>Перцентиль расстояний-границ (0..1]: границы ставятся там, где distance >= percentile.</summary>
    public double Percentile { get; init; } = 0.90;

    /// <summary>Минимальная дистанция, ниже которой граница не ставится (защита от шума).</summary>
    public double MinDistance { get; init; } = 0.02;

    /// <summary>Размер окна TextTiling (1 — соседние атомы без окна).</summary>
    public int Window { get; init; } = 1;

    /// <summary>Максимальный объём входа для семантического режима, байт.</summary>
    public long MaxBytes { get; init; } = 20_000_000;

    /// <summary>Поведение при превышении <see cref="MaxBytes"/>.</summary>
    public SemanticFallbackMode Fallback { get; init; } = SemanticFallbackMode.Recursive;

    /// <summary>Атомы короче этого числа символов склеиваются с соседями.</summary>
    public int MinAtomChars { get; init; } = 20;

    /// <summary>Атомы длиннее этого числа токенов предварительно делятся.</summary>
    public int MaxAtomTokens { get; init; } = 512;

    /// <summary>Размер окна streaming-режима в байтах UTF-8 (по умолчанию 4 МБ).</summary>
    public int StreamWindowBytes { get; init; } = 4_000_000;

    /// <summary>Перекрытие соседних streaming-окон в байтах UTF-8.</summary>
    public int StreamOverlapBytes { get; init; } = 256_000;

    /// <summary>Проверить согласованность параметров (невалидные значения → ArgumentException, exit 2 / 400).</summary>
    public void Validate()
    {
        if (Percentile <= 0 || Percentile > 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(Percentile),
                Percentile,
                "--semantic-percentile должен быть в диапазоне (0, 1].");
        }

        if (MinDistance < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MinDistance),
                MinDistance,
                "--semantic-min-distance не может быть отрицательным.");
        }

        if (Window < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(Window), Window, "--semantic-window должен быть не меньше 1.");
        }

        if (MaxBytes <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxBytes), MaxBytes, "--semantic-max-bytes должен быть больше нуля.");
        }

        if (MinAtomChars < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(MinAtomChars), MinAtomChars, "--semantic-min-atom-chars не может быть отрицательным.");
        }

        if (MaxAtomTokens < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(MaxAtomTokens), MaxAtomTokens, "--semantic-max-atom-tokens должен быть не меньше 1.");
        }

        if (StreamWindowBytes < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StreamWindowBytes),
                StreamWindowBytes,
                "--semantic-stream-window-bytes должен быть больше нуля.");
        }

        if (StreamOverlapBytes < 0 || StreamOverlapBytes >= StreamWindowBytes)
        {
            throw new ArgumentOutOfRangeException(
                nameof(StreamOverlapBytes),
                StreamOverlapBytes,
                "--semantic-stream-overlap-bytes должен быть в диапазоне [0, window).");
        }
    }

    /// <summary>Разбор значения <c>--semantic-fallback</c>.</summary>
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
                $"Неизвестный режим --semantic-fallback '{value}'. Допустимые значения: recursive, error, streaming.")
        };
    }
}
