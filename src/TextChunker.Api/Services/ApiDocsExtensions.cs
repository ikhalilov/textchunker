using Microsoft.OpenApi;

using Scalar.AspNetCore;

using System.Reflection;

namespace TextChunker.Api.Services;

/// <summary>Подключение OpenAPI (Swashbuckle) и Scalar UI.</summary>
public static class ApiDocsExtensions
{
    public const string DocumentName = "v1";
    private static readonly string[] setupAction =
        [
            "TextChunker.Api.xml",
            "TextChunker.Abstractions.xml",
            "TextChunker.xml"
        ];

    public static IServiceCollection AddApiDocs(this IServiceCollection services, IConfiguration configuration)
    {
        services.Configure<ApiDocsOptions>(configuration.GetSection("ApiDocs"));

        services.AddEndpointsApiExplorer();
        services.AddSwaggerGen(options =>
        {
            ApiDocsOptions docs = configuration.GetSection("ApiDocs").Get<ApiDocsOptions>() ?? new ApiDocsOptions();
            string version = (typeof(ApiDocsExtensions).Assembly
                .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                ?? "0.0.0").Split('+')[0];

            options.SwaggerDoc(DocumentName, new OpenApiInfo
            {
                Title = docs.Title,
                Version = version,
                Description =
                    "Нарезка текста на чанки для RAG: стратегии `fixed`, `recursive`, `paragraph` и `semantic`, " +
                    "токенизаторы, лимиты, `--prefix`, перекрытие, кодировки. Ответы — JSON или NDJSON; " +
                    "ошибки — `application/problem+json`."
            });

            foreach (string xml in setupAction)
            {
                string path = Path.Combine(AppContext.BaseDirectory, xml);
                if (File.Exists(path))
                {
                    options.IncludeXmlComments(path);
                }
            }
        });

        return services;
    }

    /// <summary>Публикует OpenAPI и Scalar; при <c>ApiDocs:Enabled=false</c> ничего не регистрирует.</summary>
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

        app.MapSwagger(options.OpenApiRoute);
        app.MapScalarApiReference(options.Route, scalar =>
        {
            scalar
                .WithTitle(options.Title)
                .WithOpenApiRoutePattern(options.OpenApiRoute)
                .AddDocument(DocumentName, options.Title)
                .WithDefaultHttpClient(ScalarTarget.Shell, ScalarClient.Curl);
        });

        // Корень удобнее перенаправить в документацию, чем отдавать 404.
        app.MapGet("/", () => Results.Redirect(options.Route))
            .ExcludeFromDescription();

        return app;
    }
}
