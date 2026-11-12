using TextChunker.Abstractions;

namespace TextChunker.Sources;

/// <summary>
/// Источник текста из stdin (pipe). Читает только когда stdin переопределён
/// (pipe/file); интерактивный терминал без ввода — ошибка (exit 1),
/// чтобы не блокировать.
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
                "stdin не переопределён (интерактивный терминал). Передайте --input <файл> или направьте текст в stdin, например: " +
                "echo 'text' | textchunker --input -");
        }
    }

    public StdinTextSource(TextSourceOptions opts)
    {
        EnsureAvailable();
        _opts = opts;
        _reader = Console.In;

        // BOM на stdin может прийти как U+FFFD (невалидная последовательность в UTF-8)
        // или как U+FEFF ( EF BB BF, корректно декодированное). В любом случае —
        // не должен попадать в текст чанков: отбрасываем первый символ, если это BOM.
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
        // DecoderFallbackException → TextSourceErrorException(4) перехватывается внутри TextLines.ReadLines.
        return TextLines.ReadLines(_reader, _opts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        // Console.In не закрываем — это stdin приложения.
    }
}
