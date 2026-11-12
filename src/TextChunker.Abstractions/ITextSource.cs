namespace TextChunker.Abstractions;

/// <summary>
/// Text source. Reading is streaming line by line; the BOM is discarded on creation
/// (BomDetected — for diagnostics). Offsets are in the normalized space:
/// text = join(content_i, "\n").
/// </summary>
public interface ITextSource
{
    /// <summary>Source name for the `source` metadata (file name or "stdin").</summary>
    string SourceName { get; }

    /// <summary>A BOM was detected at the start of the stream (UTF-8 EF BB BF); if present, it was discarded.</summary>
    bool BomDetected { get; }

    /// <summary>
    /// (content, startChar, endChar): content is the line without its line ending (normalized);
    /// startChar is the CharIndex where content starts in the joined text; endChar is exclusive (startChar + content.Length).
    /// </summary>
    IEnumerable<(string Content, long StartChar, long EndChar)> Lines();
}
