namespace TextChunker.Sources;

/// <summary>Chain of streams: first from the MemoryStream (head bytes), then from the source Stream.</summary>
internal sealed class ConcatStream : Stream
{
    private readonly Stream _first;
    private readonly Stream _second;

    public ConcatStream(Stream first, Stream second)
    {
        _first = first;
        _second = second;
    }

    public override bool CanSeek => false;
    public override bool CanRead => true;
    public override bool CanWrite => false;

    public override long Length => -1;

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    // Empty stub: ConcatStream is read-only, there is nothing to flush.
    // The CanWrite guard protects against recursion in the dispose path (Close() -> Flush()),
    // where base.Stream.Dispose() itself calls Flush()/Close().
    public override void Flush()
    {
        if (!CanWrite)
        {
            return;
        }
        _first?.Flush();
        _second?.Flush();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        int written = 0;
        while (written < count)
        {
            int n = _first.Read(buffer, offset + written, count - written);
            if (n > 0)
            {
                written += n;
                continue;
            }
            break;
        }

        if (written == 0)
        {
            return _second.Read(buffer, offset, count);
        }

        return written;
    }

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (!disposing)
        {
            return;
        }
        if (!ReferenceEquals(_first, _second))
        {
            _first?.Dispose();
        }
        base.Dispose(disposing);
    }
}
