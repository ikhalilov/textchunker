using TextChunker.Abstractions;

namespace TextChunker.Chunking;

/// <summary>
/// Источник над уже прочитанным текстом: нужен семантической стратегии, работающей
/// в document-mode после буферизации. Смещения — в join-пространстве («\n»).
/// </summary>
internal sealed class BufferedTextSource : ITextSource
{
    private readonly List<(string Content, long StartChar, long EndChar)> _lines = [];

    public BufferedTextSource(string text, string sourceName)
        : this(text, sourceName, baseOffset: 0)
    {
    }

    public BufferedTextSource(string text, string sourceName, long baseOffset)
    {
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? "text" : sourceName;

        long start = 0;
        foreach (string line in text.Split('\n'))
        {
            _lines.Add((line, baseOffset + start, baseOffset + start + line.Length));
            start += line.Length + 1;
        }
    }

    public string SourceName { get; }

    public bool BomDetected => false;

    public IEnumerable<(string Content, long StartChar, long EndChar)> Lines() => _lines;
}
