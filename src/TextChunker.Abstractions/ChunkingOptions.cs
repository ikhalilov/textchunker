namespace TextChunker.Abstractions;

/// <summary>Настройки нарезки. Одна единица измерения активна по max-* параметру.</summary>
public sealed record ChunkingOptions
{
    /// <summary>`fixed`, `recursive`, `paragraph` (MVP).</summary>
    public string Strategy { get; init; } = "recursive";

    public int? MaxTokens { get; init; }
    public int? MaxChars { get; init; }
    public int? MaxWords { get; init; }
    /// <summary>Опциональная единица — байты (сетевые/бинарные лимиты).</summary>
    public int? MaxBytes { get; init; }

    public int? MinTokens { get; init; }
    public int? MinChars { get; init; }
    public int? MinWords { get; init; }

    public int? OverlapTokens { get; init; }
    public int? OverlapChars { get; init; }

    /// <summary>Обрезка пробелов по краям чанка (включено по умолчанию).</summary>
    public bool Trim { get; init; } = true;

    /// <summary>Отбросить последний чанк, меньший min, без объединения с предыдущим.</summary>
    public bool DropSmall { get; init; }

    /// <summary>Task-префикс, учитываемый в лимите: tokenCount(prefix + chunk) &lt;= max-tokens.</summary>
    public string? Prefix { get; init; }

    /// <summary>Иерархические разделители `recursive`, от крупного к мелкому. Пустая строка = жёсткая разбивка.</summary>
    public IReadOnlyList<string> Separators { get; init; } = DefaultSeparators;

    public static readonly IReadOnlyList<string> DefaultSeparators = [
        "\n\n", "\n", ". ", "! ", "? ", "; ", ", ", " ", ""
    ];

    public SizeUnit Unit { get; }
    public int? MaxSize { get; }

    /// <summary>Активная единица измерения по max-* (priority: tokens > chars > words > bytes).</summary>
    static SizeUnit DetermineUnit(ChunkingOptions o) =>
        o.MaxTokens is { } t ? SizeUnit.Tokens :
        o.MaxChars is { } c ? SizeUnit.Chars :
        o.MaxWords is { } w ? SizeUnit.Words :
        o.MaxBytes is { } b ? SizeUnit.Bytes :
        SizeUnit.Tokens; // default для recursive/paragraph без явного max — tokens (fallback: word/char)

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
