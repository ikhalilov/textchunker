namespace TextChunker.Sources;

/// <summary>Опции источника текста: кодировка, режим невалидного UTF-8, нормализация.</summary>
public sealed class TextSourceOptions
{
    /// <summary>Имя кодировки: "utf-8", "windows-1251", "latin1" и т.п. null → UTF-8.</summary>
    public string? EncodingName { get; init; } = "utf-8";

    /// <summary>true → строгий: невалидная последовательность = ошибка (exit 4). false → U+FFFD.</summary>
    public bool InvalidAsError { get; init; }

    public bool NormalizeNfc { get; init; }
    public bool NormalizeNfkc { get; init; }
    /// <summary>Нормализация line-конецников в LF.</summary>
    public bool NormalizeLineEndings { get; init; }
    /// <summary>Объединение многократных пробелов/табов внутри строки в один.</summary>
    public bool NormalizeWhitespace { get; init; }

    public bool AnyNormalization => NormalizeNfc || NormalizeNfkc || NormalizeLineEndings || NormalizeWhitespace;

    public string? NormalizationDescription =>
        NormalizeNfc && NormalizeNfkc && NormalizeLineEndings && NormalizeWhitespace ? null
        : NormalizeNfc ? "nfc"
        : NormalizeNfkc ? "nfkc"
        : NormalizeLineEndings ? "line-endings"
        : NormalizeWhitespace ? "whitespace"
        : null;
}
