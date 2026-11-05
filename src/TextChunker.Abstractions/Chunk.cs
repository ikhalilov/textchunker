namespace TextChunker.Abstractions;

/// <summary>
/// Чанк — фрагмент исходного текста после нарезки. Поля: id/seq/source обязательны;
/// размеры вычисляются при сборке. Смещения — относительно исходного текста (или нормализованного,
/// если `--normalize` включён).
/// </summary>
public sealed record Chunk
{
    /// <summary>Детерминированный идентификатор: по умолчанию "{source}:{seq:D6}".</summary>
    public required string Id { get; init; }

    /// <summary>Порядковый номер чанка в последовательности, начиная с 1.</summary>
    public required int Seq { get; init; }

    /// <summary>Имя источника (путь файла или "stdin").</summary>
    public required string Source { get; init; }

    public required string Text { get; init; }

    /// <summary>Начальный CharIndex (включительно) в исходном/нормализованном тексте.</summary>
    public long? StartChar { get; init; }

    /// <summary>Конец CharIndex (исключительно) в исходном/нормализованном тексте.</summary>
    public long? EndChar { get; init; }

    /// <summary>Количество символов (Unicode code units / grafem). Устанавливается из Text.Length.</summary>
    public int? CharCount { get; init; }

    /// <summary>Количество байтов UTF-8 текста.</summary>
    public int? ByteCount { get; init; }

    /// <summary>Точный count токенов модельным токенизатором (включая prefix), если он подключён.</summary>
    public int? TokenCount { get; init; }

    /// <summary>Метаданные: стратегия, токенизатор, модель, prefix/prefixTokens, normalized, user metadata.</summary>
    public required IReadOnlyDictionary<string, object?> Metadata { get; init; }
}
