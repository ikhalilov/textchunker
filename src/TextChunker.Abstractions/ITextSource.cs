namespace TextChunker.Abstractions;

/// <summary>
/// Источник текста. Чтение — потоковое по строкам; BOM отбрасывается при создании
/// (BomDetected — для диагностики). Смещения — в нормализованном пространстве:
/// текст = join(content_i, "\n").
/// </summary>
public interface ITextSource
{
    /// <summary>Имя источника для metadata `source` (имя файла или "stdin").</summary>
    string SourceName { get; }

    /// <summary>BOM обнаружен в начале потока (UTF-8 EF BB BF), если он был — отброшен.</summary>
    bool BomDetected { get; }

    /// <summary>
    /// (content, startChar, endChar): content — строка без line ending (нормализованная);
    /// startChar — CharIndex начала content в join-тексте; endChar — exclusive (startChar + content.Length).
    /// </summary>
    IEnumerable<(string Content, long StartChar, long EndChar)> Lines();
}
