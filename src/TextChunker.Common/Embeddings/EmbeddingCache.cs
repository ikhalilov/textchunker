using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TextChunker.Embeddings;

/// <summary>
/// Disk embedding cache: <c>&lt;root&gt;/&lt;scope&gt;/&lt;sha256&gt;.bin</c>, where scope is
/// the model/provider and the hash is computed from <c>model \0 prompt \0 text</c>.
/// Record format: the magic <c>TCEM1</c>, int32 dimensions (LE), then float32[] (LE).
/// Writing is atomic (`.tmp` + <see cref="File.Move(string, string, bool)"/>); corrupted
/// records are deleted and recomputed.
/// </summary>
public sealed class EmbeddingCache
{
    private static readonly byte[] Magic = { (byte)'T', (byte)'C', (byte)'E', (byte)'M', (byte)'1' };
    private const int HeaderLength = 5 + 4;

    private readonly string? _scopeDir;
    private readonly string _model;
    private readonly string _prompt;

    public EmbeddingCache(string model, string? prompt, string? cacheDir)
    {
        _model = model ?? string.Empty;
        _prompt = prompt ?? string.Empty;

        string root = string.IsNullOrWhiteSpace(cacheDir) ? DefaultRoot() : cacheDir!.Trim();
        _scopeDir = Path.Combine(root, Sanitize(_model));
    }

    /// <summary>Cache root: an explicit directory or LocalApplicationData/TextChunker/embeddings.</summary>
    public static string ResolveRoot(string? cacheDir)
        => string.IsNullOrWhiteSpace(cacheDir) ? DefaultRoot() : cacheDir!.Trim();

    /// <summary>Default cache directory: LocalApplicationData/TextChunker/embeddings.</summary>
    public static string DefaultRoot()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        baseDir = string.IsNullOrWhiteSpace(baseDir) ? Path.GetTempPath() : baseDir;
        return Path.Combine(baseDir, "TextChunker", "embeddings");
    }

    /// <summary>Cache scope directory (model/provider); null — the cache is unavailable.</summary>
    public string? ScopeDirectory => _scopeDir;

    /// <summary>Path of the cache file for a text.</summary>
    public string GetPath(string text)
    {
        if (_scopeDir is null)
        {
            throw new InvalidOperationException("The embedding cache is not configured.");
        }
        return Path.Combine(_scopeDir, Hash(text) + ".bin");
    }

    /// <summary>Read a vector; false — a miss or a corrupted record (the file is then deleted).</summary>
    public bool TryGet(string text, out float[] vector)
    {
        vector = Array.Empty<float>();
        if (_scopeDir is null)
        {
            return false;
        }

        string path = GetPath(text);
        if (!File.Exists(path))
        {
            return false;
        }

        try
        {
            byte[] bytes = File.ReadAllBytes(path);
            if (bytes.Length < HeaderLength)
            {
                throw new InvalidDataException("Cache record is too short.");
            }

            for (int i = 0; i < Magic.Length; i++)
            {
                if (bytes[i] != Magic[i])
                {
                    throw new InvalidDataException("Invalid cache record magic.");
                }
            }

            int dimensions = BitConverter.ToInt32(bytes, Magic.Length);
            if (dimensions <= 0 || bytes.Length != HeaderLength + dimensions * 4)
            {
                throw new InvalidDataException($"Invalid vector size in the cache (dim={dimensions}).");
            }

            var values = new float[dimensions];
            Buffer.BlockCopy(bytes, HeaderLength, values, 0, dimensions * 4);
            foreach (float value in values)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new InvalidDataException("The cache contains NaN/Infinity.");
                }
            }

            vector = values;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Corrupted/unavailable record: delete it and treat it as a miss (it will be rewritten).
            TryDelete(path);
            return false;
        }
    }

    /// <summary>Write a vector. I/O errors do not interrupt chunking — the record is silently discarded.</summary>
    public void Set(string text, float[] vector)
    {
        if (_scopeDir is null || vector is null || vector.Length == 0)
        {
            return;
        }

        string path = GetPath(text);
        string tmpPath = path + ".tmp";
        try
        {
            Directory.CreateDirectory(_scopeDir);

            using (var stream = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: false))
            {
                writer.Write(Magic);
                writer.Write(vector.Length);
                foreach (float value in vector)
                {
                    writer.Write(value);
                }
                writer.Flush();
                stream.Flush(flushToDisk: true);
            }

            File.Move(tmpPath, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            TryDelete(tmpPath);
        }
    }

    /// <summary>Hash of the cache key (model \0 prompt \0 text), lowercase hex.</summary>
    public string Hash(string text)
    {
        byte[] key = Encoding.UTF8.GetBytes($"{_model}\u0000{_prompt}\u0000{text}");
        byte[] digest = SHA256.HashData(key);
        var builder = new StringBuilder(digest.Length * 2);
        foreach (byte b in digest)
        {
            builder.Append(b.ToString("x2", CultureInfo.InvariantCulture));
        }
        return builder.ToString();
    }

    /// <summary>Batch read: null — a miss for that index.</summary>
    public float[]?[] GetMany(IReadOnlyList<string> texts)
    {
        var results = new float[]?[texts.Count];
        for (int i = 0; i < texts.Count; i++)
        {
            if (TryGet(texts[i], out float[] vector))
            {
                results[i] = vector;
            }
        }
        return results;
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }

    private static string Sanitize(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "default";
        }

        var builder = new StringBuilder(name.Length);
        foreach (char c in name.Trim())
        {
            builder.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
        }
        return builder.ToString();
    }
}
