using TextChunker.Abstractions;

namespace TextChunker.Tests;

internal sealed class FakeSource : ITextSource
{
    private readonly List<(string Content, long StartChar, long EndChar)> _lines = [];

    public FakeSource(params string[] lines)
    {
        long start = 0;
        foreach (string line in lines)
        {
            _lines.Add((line, start, start + line.Length));
            start += line.Length + 1;
        }
    }

    public string SourceName => "test";

    public bool BomDetected => false;

    public IEnumerable<(string Content, long StartChar, long EndChar)> Lines() => _lines;
}
