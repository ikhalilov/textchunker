using System.Reflection;

using Microsoft.OpenApi;

using Scalar.AspNetCore;

namespace TextChunker.Api.Services;

/// <summary>Wiring up the built-in OpenAPI (Microsoft.AspNetCore.OpenApi) and the Scalar UI.</summary>
public static class ApiDocsExtensions
{
    public const string DocumentName = "v1";

    private const string Description =
        "Text chunking for RAG: `fixed`, `recursive`, `paragraph` and `semantic` strategies, " +
        "tokenizers, limits, `--prefix`, overlap, encodings. Responses are JSON or NDJSON; " +
        "errors are `application/problem+json`.";

    public static IServiceCollection AddApiDocs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiDocsOptions>(configuration.GetSection("ApiDocs"));
        services.AddEndpointsApiExplorer();

        ApiDocsOptions docs = configuration.GetSection("ApiDocs").Get<ApiDocsOptions>() ?? new ApiDocsOptions();
        string version = (typeof(ApiDocsExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? "0.0.0").Split('+')[0];

        services.AddOpenApi(DocumentName, options =>
        {
            options.AddDocumentTransformer((document, _, _) =>
            {
                document.Info = new OpenApiInfo
                {
                    Title = docs.Title,
                    Version = version,
                    Description = Description
                };
                return Task.CompletedTask;
            });
        });

        return services;
    }

    /// <summary>Publishes OpenAPI and Scalar; when <c>ApiDocs:Enabled=false</c>, registers nothing.</summary>
    public static WebApplication MapApiDocs(this WebApplication app)
    {
        ApiDocsOptions options = app.Configuration.GetSection("ApiDocs").Get<ApiDocsOptions>() ?? new ApiDocsOptions();
        if (!options.Enabled)
        {
            return app;
        }

        if (!options.OpenApiRoute.Contains("{documentName}", StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "ApiDocs:OpenApiRoute должен содержать {documentName}, например /openapi/{documentName}.json.");
        }

        app.MapOpenApi(options.OpenApiRoute);
        app.MapScalarApiReference(options.Route, scalar =>
        {
            scalar
                .WithTitle(options.Title)
                .WithOpenApiRoutePattern(options.OpenApiRoute)
                .AddDocument(DocumentName, options.Title)
                .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl);
        });

        // The root is better redirected to the documentation than answered with a 404.
        app.MapGet("/", () => Results.Redirect(options.Route))
            .ExcludeFromDescription();

        return app;
    }
}
