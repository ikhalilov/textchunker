namespace TextChunker.Tokenizers;

using Microsoft.ML.Tokenizers;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;

using TextChunker;
using TextChunker.Abstractions;

/// <summary>
/// The <c>huggingface</c> model tokenizer: loads the model's <c>tokenizer.json</c>
/// (<c>google/embeddinggemma-2</c> by default) and counts tokens with the exact BPE engine of
/// <c>Microsoft.ML.Tokenizers</c> (Rust <c>tokenizers</c> — the same engine as HF <c>transformers</c>,
/// reading the same <c>tokenizer.json</c>). On first use it downloads the file into the cache
/// (<c>--tokenizer-cache-dir</c>); afterwards it works offline.
/// </summary>
public sealed class HuggingFaceTokenizer : ITokenizer
{
    private readonly BpeTokenizer _tokenizer;
    private readonly string _modelName;
    private readonly string _jsonPath;
    private readonly DateTime _loadedAt;

    /// <summary>Default model for <c>--tokenizer huggingface</c>.</summary>
    public const string DefaultModelName = "google/embeddinggemma-2";

    private const string HfBaseUrl = "https://huggingface.co";
    private static readonly HttpClientSource _httpClientSource = new();
    private readonly HttpClient _httpClient;

    public string Name => "huggingface";

    public string ModelName => _modelName;

    public HuggingFaceTokenizer(string? model, string? cacheDir)
        : this(model, cacheDir, null)
    {
    }

    /// <summary>Testable overload: allows the HTTP client to be substituted (no network).</summary>
    internal HuggingFaceTokenizer(string? model, string? cacheDir, HttpClient? httpClient)
    {
        _httpClient = httpClient ?? _httpClientSource.Get();
        string? rawModel = string.IsNullOrWhiteSpace(model) ? null : model.Trim();

        bool localPath = rawModel != null
            && File.Exists(rawModel)
            && rawModel.EndsWith(".json", StringComparison.OrdinalIgnoreCase);

        if (localPath)
        {
            _modelName = rawModel!;
            _jsonPath = Path.GetFullPath(rawModel!);
        }
        else
        {
            _modelName = rawModel ?? DefaultModelName;
            string dir = cacheDir ?? ResolveDefaultCacheDir();
            _jsonPath = Path.Combine(dir, Sanitize(_modelName), "tokenizer.json");
            DownloadIfMissing(_jsonPath, _modelName);
        }

        try
        {
            _tokenizer = LoadBpeTokenizer(_jsonPath);
        }
        catch (Exception ex)
        {
            throw new TokenizerLoadException(
                $"Failed to load tokenizer '{_modelName}' ({_jsonPath}): {ex.Message}", ex);
        }

        _loadedAt = DateTime.Now;
    }

    public int CountTokens(string text) => _tokenizer.CountTokens(text, true, true);

    public int? CutAtTokenBoundary(string text, int n)
    {
        if (string.IsNullOrEmpty(text))
        {
            return null;
        }
        int count = _tokenizer.CountTokens(text, true, true);
        if (n <= 0)
        {
            return 0;
        }
        if (n >= count)
        {
            return text.Length;
        }
        return _tokenizer.GetIndexByTokenCount(text, n, out _, out _, true, true);
    }

    /// <summary>Default cache directory: %LOCALAPPDATA%/TextChunker/tokenizers (cross-platform LocalApplicationData).</summary>
    private static string ResolveDefaultCacheDir()
    {
        string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        baseDir = string.IsNullOrWhiteSpace(baseDir) ? Path.GetTempPath() : baseDir;
        return Path.Combine(baseDir, "TextChunker", "tokenizers");
    }

    private static string Sanitize(string name)
    {
        return name.Replace('/', '_').Replace('\\', '_');
    }

    private void DownloadIfMissing(string jsonPath, string modelName)
    {
        if (File.Exists(jsonPath))
        {
            return;
        }

        string? dir = Path.GetDirectoryName(jsonPath);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string encodedModel = string.Join("/", modelName.Split('/').Select(Uri.EscapeDataString));
        string url = $"{HfBaseUrl}/{encodedModel}/resolve/main/tokenizer.json";

        string tmpPath = jsonPath + ".tmp";
        try
        {
            using var response = _httpClient.Send(new HttpRequestMessage(HttpMethod.Get, url));
            response.EnsureSuccessStatusCode();

            using var stream = response.Content.ReadAsStream();
            if (stream == null)
            {
                throw new TokenizerLoadException($"Empty response while downloading '{modelName}' from {url}.");
            }

            using (var fs = new FileStream(tmpPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                stream.CopyTo(fs);
                fs.Flush(flushToDisk: true);
            }

            File.Move(tmpPath, jsonPath, overwrite: true);
        }
        catch (Exception ex)
        {
            if (File.Exists(tmpPath))
            {
                File.Delete(tmpPath);
            }
            throw new TokenizerLoadException(
                $"Failed to download tokenizer '{modelName}' from {url}: {ex.Message}", ex);
        }
    }

    /// <summary>Loading <c>tokenizer.json</c> into <c>BpeTokenizer</c>.
    /// It mirrors the structure of HF <c>tokenizer.json</c> field by field: <c>model.type=BPE</c>,
    /// <c>vocab</c> as an object {token: id}, <c>merges</c> as an array of pairs, <c>added_tokens</c> as special.
    /// </summary>
    private static BpeTokenizer LoadBpeTokenizer(string jsonPath)
    {
        string raw = File.ReadAllText(jsonPath);
        JsonDocument rootDocument = JsonDocument.Parse(raw);
        JsonElement root = rootDocument.RootElement;
        JsonElement model = root.GetProperty("model");

        bool byteFallback = model.GetProperty("byte_fallback").GetBoolean();
        string unk = model.GetProperty("unk_token").GetString()!;
        bool fuseUnk = model.GetProperty("fuse_unk").GetBoolean();

        string? continuingSubwordPrefix = null;
        var cspProp = model.GetProperty("continuing_subword_prefix");
        if (cspProp.ValueKind == JsonValueKind.String)
        {
            continuingSubwordPrefix = cspProp.GetString();
        }

        string? endOfWordSuffix = null;
        var eosProp = model.GetProperty("end_of_word_suffix");
        if (eosProp.ValueKind == JsonValueKind.String)
        {
            endOfWordSuffix = eosProp.GetString();
        }

        var vocabEl = model.GetProperty("vocab");
        var vocabList = new List<KeyValuePair<string, int>>();
        foreach (var kv in vocabEl.EnumerateObject())
        {
            vocabList.Add(new KeyValuePair<string, int>(kv.Name, kv.Value.GetInt32()));
        }

        var mergesEl = model.GetProperty("merges");
        var mergesList = new List<string>();
        foreach (var m in mergesEl.EnumerateArray())
        {
            mergesList.Add(m[0].GetString() + " " + m[1].GetString());
        }

        var specialTokens = new Dictionary<string, int>();
        var addedEl = root.GetProperty("added_tokens");
        foreach (var el in addedEl.EnumerateArray())
        {
            string content = el.GetProperty("content").GetString()!;
            int id = el.GetProperty("id").GetInt32();
            specialTokens[content] = id;
        }

        var options = new BpeOptions(vocabList);
        options.Merges = mergesList;
        options.ByteLevel = byteFallback;
        options.UnknownToken = unk;
        options.FuseUnknownTokens = fuseUnk;
        options.SpecialTokens = specialTokens;
        options.ContinuingSubwordPrefix = continuingSubwordPrefix;
        options.EndOfWordSuffix = endOfWordSuffix;

        return BpeTokenizer.Create(options);
    }

    /// <summary><c>HttpClient</c> wrapper with a shared timeout and lazy creation.</summary>
    private sealed class HttpClientSource
    {
        private readonly Lazy<HttpClient> _lazy;

        public HttpClientSource()
        {
            _lazy = new Lazy<HttpClient>(() =>
            {
                var c = new HttpClient();
                c.Timeout = TimeSpan.FromSeconds(180);
                return c;
            });
        }

        public HttpClient Get() => _lazy.Value;
    }
}
