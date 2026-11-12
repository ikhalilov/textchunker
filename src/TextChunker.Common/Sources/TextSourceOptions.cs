namespace TextChunker.Sources;

/// <summary>Text source options: encoding, invalid UTF-8 mode, normalization.</summary>
public sealed class TextSourceOptions
{
    /// <summary>Encoding name: "utf-8", "windows-1251", "latin1", etc. null → UTF-8.</summary>
    public string? EncodingName { get; init; } = "utf-8";

    /// <summary>true → strict: an invalid sequence is an error (exit 4). false → U+FFFD.</summary>
    public bool InvalidAsError { get; init; }

    public bool NormalizeNfc { get; init; }
    public bool NormalizeNfkc { get; init; }
    /// <summary>Normalization of line endings to LF.</summary>
    public bool NormalizeLineEndings { get; init; }
    /// <summary>Collapsing multiple spaces/tabs within a line into one.</summary>
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
