using System.Collections.Concurrent;
using TextChunker.Abstractions;
using TextChunker.Tokenizers;

namespace TextChunker.Api.Services;

/// <summary>
/// Кэш созданных токенизаторов (singleton). Загрузка HF-модели занимает
/// ~2 с и сотни МБ, поэтому повторные запросы переиспользуют экземпляр.
/// Ключ — (имя, модель/путь, cacheDir).
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
            // Не кэшируем неудачную загрузку: следующий запрос может успеть (сеть/кэш).
            _cache.TryRemove(key, out _);
            throw;
        }
    }
}
