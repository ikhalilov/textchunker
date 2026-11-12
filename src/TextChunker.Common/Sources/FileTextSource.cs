using TextChunker.Abstractions;

namespace TextChunker.Sources;

/// <summary>Text source from a file (streaming read, BOM, encoding).</summary>
public sealed class FileTextSource : ITextSource, IDisposable
{
    private readonly Stream _stream;
    private readonly TextSourceOptions _opts;
    private StreamReader? _reader;
    private bool _disposed;

    /// <summary>Source name: the file name (Path.GetFileName).</summary>
    public string SourceName { get; }

    private bool _bom;

    public FileTextSource(string path, TextSourceOptions opts)
    {
        if (!File.Exists(path))
        {
            throw new TextSourceErrorException(3, $"Файл не найден: {path}");
        }

        SourceName = Path.GetFileName(path);
        _opts = opts;

        Stream stream;
        try
        {
            stream = File.OpenRead(path);
        }
        catch (Exception)
        {
            throw new TextSourceErrorException(4, $"Не удалось открыть файл для чтения: {path}");
        }

        try
        {
            _reader = TextLines.CreateReader(stream, opts, out _bom);
            _stream = stream;
        }
        catch
        {
            // CreateReader threw (for example, exit 2 — invalid encoding):
            // the file is already open — it must be released, otherwise the path stays locked.
            stream.Dispose();
            throw;
        }
    }

    public bool BomDetected => _bom;

    public IEnumerable<(string Content, long StartChar, long EndChar)> Lines()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(FileTextSource));
        // DecoderFallbackException → TextSourceErrorException(4) is caught inside TextLines.ReadLines.
        return TextLines.ReadLines(_reader!, _opts);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _reader?.Dispose();
        _stream.Dispose();
    }
}
