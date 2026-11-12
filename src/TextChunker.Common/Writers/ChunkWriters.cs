using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;

using TextChunker.Abstractions;

namespace TextChunker.Writers;

/// <summary>Single JSON serialization of a chunk (camelCase, no nulls, UTF-8-friendly escaping).</summary>
public static class ChunkJson
{
    public static JsonSerializerOptions Options { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        WriteIndented = false
    };

    public static string Serialize(Chunk chunk) => JsonSerializer.Serialize(chunk, Options);
}

/// <summary>JSON Lines: one JSON record per chunk, UTF-8 without BOM, one object per line.</summary>
public sealed class JsonLinesWriter : IChunkWriter, IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private bool _disposed;

    public JsonLinesWriter(TextWriter writer, bool ownsWriter = false)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownsWriter = ownsWriter;
    }

    public void Write(Chunk chunk)
    {
        if (chunk is null)
        {
            throw new ArgumentNullException(nameof(chunk));
        }

        _writer.Write(ChunkJson.Serialize(chunk));
        _writer.Write('\n');
    }

    public void Flush() => _writer.Flush();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _writer.Flush();
        if (_ownsWriter)
        {
            _writer.Dispose();
        }
    }
}

/// <summary>Text format: the <c>=== CHUNK NNNNNN ===</c> marker and the chunk text.</summary>
public sealed class TextChunkWriter : IChunkWriter, IDisposable
{
    private readonly TextWriter _writer;
    private readonly bool _ownsWriter;
    private bool _disposed;

    public TextChunkWriter(TextWriter writer, bool ownsWriter = false)
    {
        _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        _ownsWriter = ownsWriter;
    }

    public void Write(Chunk chunk)
    {
        if (chunk is null)
        {
            throw new ArgumentNullException(nameof(chunk));
        }

        _writer.Write("=== CHUNK ");
        _writer.Write(chunk.Seq.ToString("D6"));
        _writer.Write(" ===\n");
        _writer.Write(chunk.Text);
        _writer.Write('\n');
    }

    public void Flush() => _writer.Flush();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        _writer.Flush();
        if (_ownsWriter)
        {
            _writer.Dispose();
        }
    }
}
