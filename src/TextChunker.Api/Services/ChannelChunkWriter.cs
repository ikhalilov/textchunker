using System.Threading.Channels;
using TextChunker.Abstractions;

namespace TextChunker.Api.Services;

/// <summary>
/// IChunkWriter → bounded Channel для потокового HTTP-ответа. Блокируется при
/// заполнении канала (backpressure); отмена чтения/клиента разблокирует запись.
/// </summary>
public sealed class ChannelChunkWriter(ChannelWriter<Chunk> writer, CancellationToken cancellationToken) : IChunkWriter
{
    private readonly ChannelWriter<Chunk> _writer = writer;
    private readonly CancellationToken _cancellationToken = cancellationToken;

    public void Write(Chunk chunk)
    {
        _writer.WriteAsync(chunk, _cancellationToken).AsTask().GetAwaiter().GetResult();
    }
}
