using Microsoft.AspNetCore.Mvc;

using TextChunker.Api.Contracts;

namespace TextChunker.Api.Services;

/// <summary>
/// Проверяет заголовок <c>X-API-Key</c> для semantic-запросов, если на сервере задана
/// переменная <c>TEXTCHUNKER_EMBEDDING_API_KEY</c>.
/// </summary>
public sealed class SemanticApiKeyFilter : IEndpointFilter
{
    /// <summary>Проверяет наличие и корректность <c>X-API-Key</c> для semantic-запросов.</summary>
    /// <param name="context">Контекст вызова endpoint-фильтра.</param>
    /// <param name="next">Следующий делегат в конвейере.</param>
    /// <returns>Результат следующего делегата или 401 при отсутствии ключа.</returns>
    public async ValueTask<object?> InvokeAsync(
        EndpointFilterInvocationContext context,
        EndpointFilterDelegate next)
    {
        string? strategy = context.Arguments.OfType<ChunkRequest>().FirstOrDefault()?.Strategy
            ?? context.Arguments.OfType<FileChunkForm>().FirstOrDefault()?.Strategy;

        if (strategy?.Equals("semantic", StringComparison.OrdinalIgnoreCase) == true)
        {
            string? expectedKey = Environment.GetEnvironmentVariable("TEXTCHUNKER_EMBEDDING_API_KEY");
            if (!string.IsNullOrEmpty(expectedKey))
            {
                IHeaderDictionary headers = context.HttpContext.Request.Headers;
                if (!headers.TryGetValue("X-API-Key", out var apiKeyValues) ||
                    !string.Equals(apiKeyValues.FirstOrDefault(), expectedKey, StringComparison.Ordinal))
                {
                    return Results.Problem(new ProblemDetails
                    {
                        Status = StatusCodes.Status401Unauthorized,
                        Title = "Unauthorized",
                        Detail = "Missing or invalid API key."
                    });
                }
            }
        }

        return await next(context);
    }
}
