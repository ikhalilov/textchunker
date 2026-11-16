using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;

using TextChunker.Api.Services;

namespace TextChunker.Api.Extensions;

/// <summary>
/// Registers the TextChunker.Api services in the DI container.
/// </summary>
internal static class ServiceExtensions
{
    /// <summary>
    /// Registers the API services: <c>Chunking</c> options, tokenizer cache,
    /// chunking engine, semantic API-key filter, problem details, and OpenAPI docs.
    /// </summary>
    public static IServiceCollection AddApiServices(this IServiceCollection services, IConfiguration configuration)
    {
        services
            .AddOptions<ApiOptions>()
            .Bind(configuration.GetSection("Chunking"))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        services.AddSingleton<TokenizerCache>();
        services.AddSingleton<ChunkingEngine>();
        services.AddSingleton<SemanticApiKeyFilter>();
        services.AddProblemDetails();
        services.AddApiDocs(configuration);

        return services;
    }
}
