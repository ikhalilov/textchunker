using TextChunker.Abstractions;

namespace TextChunker.Api.Services;

/// <summary>Text source built from a string: split('\n') with absolute offsets in the joined space.</summary>
public sealed class InMemoryTextSource : ITextSource
{
    private readonly List<(string Content, long StartChar, long EndChar)> _lines = [];

    public InMemoryTextSource(string text, string sourceName)
    {
        SourceName = string.IsNullOrWhiteSpace(sourceName) ? "api" : sourceName;

        long start = 0;
        foreach (string line in text.Split('\n'))
        {
            _lines.Add((line, start, start + line.Length));
            start += line.Length + 1;
        }
    }

    public string SourceName { get; }

    public bool BomDetected => false;

    public IEnumerable<(string Content, long StartChar, long EndChar)> Lines() => _lines;
}
