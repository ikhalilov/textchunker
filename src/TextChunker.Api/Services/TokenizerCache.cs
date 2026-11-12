using System.Collections.Concurrent;
using TextChunker.Abstractions;
using TextChunker.Tokenizers;

namespace TextChunker.Api.Services;

/// <summary>
/// Cache of created tokenizers (singleton). Loading an HF model takes
/// ~2 s and hundreds of MB, so repeated requests reuse the instance.
/// The key is (name, model/path, cacheDir).
/// </summary>
public sealed class TokenizerCache
{
    private readonly ConcurrentDictionary<string, Lazy<ITokenizer>> _cache =
        new(StringComparer.Ordinal);

    public ITokenizer Get(string? name, string? model, string? cacheDir)
    {
        string key = $"{name ?? string.Empty}\u001F{model ?? string.Empty}\u001F{cacheDir ?? string.Empty}";

        Lazy<ITokenizer> lazy = _cache.GetOrAdd(
            key,
            _ => new Lazy<ITokenizer>(
                () => TokenizerFactory.Create(name, model, cacheDir),
                LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return lazy.Value;
        }
        catch
        {
            // Do not cache a failed load: the next request may succeed (network/cache).
            _cache.TryRemove(key, out _);
            throw;
        }
    }
}
