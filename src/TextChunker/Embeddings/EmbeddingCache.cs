using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace TextChunker.Embeddings;

/// <summary>
/// Дисковый кэш эмбеддингов: <c>&lt;root&gt;/&lt;scope&gt;/&lt;sha256&gt;.bin</c>, где scope —
/// модель/провайдер, а хеш считается от <c>model \0 prompt \0 text</c>.
/// Формат записи: магия <c>TCEM1</c>, int32 размерность (LE), затем float32[] (LE).
/// Запись атомарная (`.tmp` + <see cref="File.Move(string, string, bool)"/>), повреждённые
/// записи удаляются и пересчитываются.
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

    /// <summary>Корень кэша: явный каталог или LocalApplicationData/TextChunker/embeddings.</summary>
    public static string ResolveRoot(string? cacheDir)
        => string.IsNullOrWhiteSpace(cacheDir) ? DefaultRoot() : cacheDir!.Trim();

    /// <summary>Каталог кэша по умолчанию: LocalApplicationData/TextChunker/embeddings.</summary>
    public static string DefaultRoot()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        baseDir = string.IsNullOrWhiteSpace(baseDir) ? Path.GetTempPath() : baseDir;
        return Path.Combine(baseDir, "TextChunker", "embeddings");
    }

    /// <summary>Каталог области кэша (модель/провайдер), null — кэш недоступен.</summary>
    public string? ScopeDirectory => _scopeDir;

    /// <summary>Путь файла кэша для текста.</summary>
    public string GetPath(string text)
    {
        if (_scopeDir is null)
        {
            throw new InvalidOperationException("Кэш эмбеддингов не настроен.");
        }
        return Path.Combine(_scopeDir, Hash(text) + ".bin");
    }

    /// <summary>Прочитать вектор; false — промах или запись повреждена (тогда файл удаляется).</summary>
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
                throw new InvalidDataException("Слишком короткая запись кэша.");
            }

            for (int i = 0; i < Magic.Length; i++)
            {
                if (bytes[i] != Magic[i])
                {
                    throw new InvalidDataException("Некорректная магия записи кэша.");
                }
            }

            int dimensions = BitConverter.ToInt32(bytes, Magic.Length);
            if (dimensions <= 0 || bytes.Length != HeaderLength + dimensions * 4)
            {
                throw new InvalidDataException($"Некорректный размер вектора в кэше (dim={dimensions}).");
            }

            var values = new float[dimensions];
            Buffer.BlockCopy(bytes, HeaderLength, values, 0, dimensions * 4);
            foreach (float value in values)
            {
                if (float.IsNaN(value) || float.IsInfinity(value))
                {
                    throw new InvalidDataException("Кэш содержит NaN/Infinity.");
                }
            }

            vector = values;
            return true;
        }
        catch (Exception exception) when (exception is IOException or InvalidDataException or UnauthorizedAccessException)
        {
            // Повреждённая/недоступная запись: удаляем и считаем промахом (будет перезаписана).
            TryDelete(path);
            return false;
        }
    }

    /// <summary>Записать вектор. Ошибки ввода-вывода не прерывают нарезку — запись молча отбрасывается.</summary>
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

    /// <summary>Хеш ключа кэша (model \0 prompt \0 text), нижний регистр hex.</summary>
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

    /// <summary>Пакетное чтение: null — промах по индексу.</summary>
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
