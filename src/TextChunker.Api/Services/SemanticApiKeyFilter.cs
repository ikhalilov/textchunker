using Microsoft.AspNetCore.Mvc;

using TextChunker.Api.Contracts;

namespace TextChunker.Api.Services;

/// <summary>
/// Validates the <c>X-API-Key</c> header for semantic requests when the
/// <c>TEXTCHUNKER_EMBEDDING_API_KEY</c> variable is set on the server.
/// </summary>
public sealed class SemanticApiKeyFilter : IEndpointFilter
{
    /// <summary>Validates the presence and correctness of <c>X-API-Key</c> for semantic requests.</summary>
    /// <param name="context">The invocation context of the endpoint filter.</param>
    /// <param name="next">The next delegate in the pipeline.</param>
    /// <returns>The result of the next delegate, or 401 when the key is missing.</returns>
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
