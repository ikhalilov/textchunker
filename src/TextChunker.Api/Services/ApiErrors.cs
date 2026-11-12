using TextChunker.Abstractions.Embeddings;

namespace TextChunker.Api.Services;

/// <summary>Translation of domain exceptions into RFC 7807 (application/problem+json).</summary>
public static class ApiErrors
{
    /// <summary>Converts an exception into an HTTP response in the ProblemDetails format.</summary>
    /// <param name="exception">The exception raised while processing the request.</param>
    /// <returns>An HTTP result with the corresponding status.</returns>
    public static IResult ToResult(Exception exception)
    {
        return exception switch
        {
            PayloadTooLargeException => Results.Problem(
                title: "Payload too large",
                detail: exception.Message,
                statusCode: StatusCodes.Status413PayloadTooLarge),

            TokenizerLoadException => Results.Problem(
                title: "Tokenizer unavailable",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable),

            EmbeddingProviderException => Results.Problem(
                title: "Embedding provider unavailable",
                detail: exception.Message,
                statusCode: StatusCodes.Status503ServiceUnavailable),

            SemanticLimitExceededException => Results.Problem(
                title: "Payload too large",
                detail: exception.Message,
                statusCode: StatusCodes.Status413PayloadTooLarge),

            UnsupportedMediaTypeException => Results.Problem(
                title: "Unsupported media type",
                detail: exception.Message,
                statusCode: StatusCodes.Status415UnsupportedMediaType),

            ArgumentException => Results.Problem(
                title: "Invalid request",
                detail: exception.Message,
                statusCode: StatusCodes.Status400BadRequest),

            OperationCanceledException => Results.StatusCode(499),

            _ => Results.Problem(
                title: "Internal server error",
                detail: "Unexpected error.",
                statusCode: StatusCodes.Status500InternalServerError)
        };
    }
}
