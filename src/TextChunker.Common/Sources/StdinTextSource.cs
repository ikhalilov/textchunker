using TextChunker.Abstractions;

namespace TextChunker.Sources;

/// <summary>
/// Text source from stdin (pipe). It reads only when stdin is redirected
/// (pipe/file); an interactive terminal without input is an error (exit 1),
/// so as not to block.
/// </summary>
public sealed class StdinTextSource : ITextSource, IDisposable
{
    private readonly TextSourceOptions _opts;
    private readonly TextReader _reader;
    private bool _disposed;
    private bool _bom;

    public string SourceName => "stdin";

    public bool BomDetected => _bom;

    private static void EnsureAvailable()
    {
        if (!Console.IsInputRedirected)
        {
            throw new TextSourceErrorException(1,
                "stdin is not redirected (interactive terminal). Pass --input <file> or pipe text into stdin, for example: " +
                "echo 'text' | textchunker --input -");
        }
    }

    public StdinTextSource(TextSourceOptions opts)
    {
        EnsureAvailable();
        _opts = opts;
        _reader = Console.In;

        // A BOM on stdin may arrive as U+FFFD (an invalid UTF-8 sequence)
        // or as U+FEFF (EF BB BF, correctly decoded). In either case it
        // must not get into the chunk text: drop the first character if it is a BOM.
        int first = _reader.Peek();
        if (first >= 0)
        {
            char c = (char)first;
            if (c == 0xFFFD || c == 0xFEFF)
            {
                var buf = new char[1];
                _reader.Read(buf, 0, 1);
                _bom = true;
            }
        }
    }

    public IEnumerable<(string Content, long StartChar, long EndChar)> Lines()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(StdinTextSource));
        // DecoderFallbackException → TextSourceErrorException(4) is caught inside TextLines.ReadLines.
        return TextLines.ReadLines(_reader, _opts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Console.In is not closed — it is the application's stdin.
    }
}
