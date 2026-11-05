using System.Security.Cryptography;
using System.Text;

using TextChunker.Abstractions.Embeddings;

namespace TextChunker.Embeddings;

/// <summary>
/// Фабрика провайдеров эмбеддингов: сопоставляет <c>--embedding-provider</c> с реализацией,
/// подставляет API-ключ из переменной окружения и оборачивает провайдер дисковым кэшем.
/// </summary>
public static class EmbeddingProviderFactory
{
    /// <summary>Поддерживаемые значения <c>--embedding-provider</c>.</summary>
    public static readonly IReadOnlyList<string> SupportedNames = new[]
    {
        "llama-cpp", "hash"
    };

    /// <summary>Провайдер по умолчанию.</summary>
    public const string DefaultProviderName = "llama-cpp";

    /// <summary>Переменная окружения с API-ключом (из тела запроса ключ не принимается).</summary>
    public const string ApiKeyEnvironmentVariable = "TEXTCHUNKER_EMBEDDING_API_KEY";

    public static IEmbeddingProvider Create(EmbeddingOptions options)
    {
        if (options is null)
        {
            throw new ArgumentNullException(nameof(options));
        }
        options.Validate();

        string key = options.Provider.Trim().ToLowerInvariant();

        IEmbeddingProvider provider = key switch
        {
            "hash" => new HashEmbeddingProvider(options.Dimensions > 0 ? options.Dimensions : 768),
            "llama-cpp" => new LlamaCppEmbeddingProvider(options, ResolveApiKey(options)),
            _ => throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Provider,
                $"Неизвестный провайдер эмбеддингов '{options.Provider}'. Допустимые значения: {string.Join(", ", SupportedNames)}.")
        };

        if (!options.UseCache)
        {
            return provider;
        }

        // Автоочистка: если заданы TTL/лимит, приводим кэш в порядок перед прогоном.
        if (options.CacheMaxBytes is not null || options.CacheTtlDays is not null)
        {
            try
            {
                EmbeddingCachePruner.Prune(
                    EmbeddingCache.ResolveRoot(options.CacheDir),
                    options.CacheMaxBytes,
                    options.CacheTtlDays is { } days ? TimeSpan.FromDays(days) : null);
            }
            catch (Exception exception) when (
                exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                // Обслуживание кэша не должно прерывать нарезку.
            }
        }

        string scope = key == "hash"
            ? $"hash-{provider.Dimensions}"
            : BuildLlamaCppScope(options, provider.Dimensions);
        var cache = new EmbeddingCache(scope, options.Prompt, options.CacheDir);
        return new CachedEmbeddingProvider(provider, cache);
    }

    /// <summary>
    /// Scope кэша llama.cpp: модель + endpoint + размерность. Одно имя модели на разных
    /// серверах/квантованиях не должно приводить к переиспользованию чужих векторов.
    /// </summary>
    private static string BuildLlamaCppScope(EmbeddingOptions options, int dimensions)
    {
        string model = string.IsNullOrWhiteSpace(options.Model) ? "default" : options.Model!.Trim();
        string identity = $"{options.Endpoint.TrimEnd('/')}\u0000{model}\u0000{dimensions}";
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        string suffix = Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
        return $"{model}-{suffix}";
    }

    /// <summary>Ключ берётся из опции (заполняется из окружения), иначе из <see cref="ApiKeyEnvironmentVariable"/>.</summary>
    private static string? ResolveApiKey(EmbeddingOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.ApiKey))
        {
            return options.ApiKey.Trim();
        }

        string? fromEnvironment = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
        return string.IsNullOrWhiteSpace(fromEnvironment) ? null : fromEnvironment.Trim();
    }
}
