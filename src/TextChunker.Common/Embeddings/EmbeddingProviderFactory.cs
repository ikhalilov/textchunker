using System.Security.Cryptography;
using System.Text;

using TextChunker.Abstractions.Embeddings;

namespace TextChunker.Embeddings;

/// <summary>
/// Factory of embedding providers: maps <c>--embedding-provider</c> to an implementation,
/// substitutes the API key from the environment variable and wraps the provider in a disk cache.
/// </summary>
public static class EmbeddingProviderFactory
{
    /// <summary>Supported values of <c>--embedding-provider</c>.</summary>
    public static readonly IReadOnlyList<string> SupportedNames = new[]
    {
        "llama-cpp", "hash"
    };

    /// <summary>Default provider.</summary>
    public const string DefaultProviderName = "llama-cpp";

    /// <summary>Environment variable holding the API key (the key is not accepted from the request body).</summary>
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

        // Automatic cleanup: if a TTL/limit is set, bring the cache in order before the run.
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
                // Cache maintenance must not interrupt chunking.
            }
        }

        string scope = key == "hash"
            ? $"hash-{provider.Dimensions}"
            : BuildLlamaCppScope(options, provider.Dimensions);
        var cache = new EmbeddingCache(scope, options.Prompt, options.CacheDir);
        return new CachedEmbeddingProvider(provider, cache);
    }

    /// <summary>
    /// llama.cpp cache scope: model + endpoint + dimensionality. The same model name on different
    /// servers/quantizations must not lead to reusing someone else's vectors.
    /// </summary>
    private static string BuildLlamaCppScope(EmbeddingOptions options, int dimensions)
    {
        string model = string.IsNullOrWhiteSpace(options.Model) ? "default" : options.Model!.Trim();
        string identity = $"{options.Endpoint.TrimEnd('/')}\u0000{model}\u0000{dimensions}";
        byte[] digest = SHA256.HashData(Encoding.UTF8.GetBytes(identity));
        string suffix = Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
        return $"{model}-{suffix}";
    }

    /// <summary>The key is taken from the option (populated from the environment), otherwise from <see cref="ApiKeyEnvironmentVariable"/>.</summary>
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
