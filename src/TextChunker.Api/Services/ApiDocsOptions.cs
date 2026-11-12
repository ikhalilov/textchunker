namespace TextChunker.Api.Services;

/// <summary>
/// Settings for the interactive API documentation (the <c>ApiDocs</c> section):
/// the OpenAPI document and the Scalar UI.
/// </summary>
public sealed class ApiDocsOptions
{
    /// <summary>true — publish OpenAPI and Scalar (enabled by default).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Scalar UI route.</summary>
    public string Route { get; set; } = "/scalar";

    /// <summary>OpenAPI document route; must contain <c>{documentName}</c>.</summary>
    public string OpenApiRoute { get; set; } = "/openapi/{documentName}.json";

    /// <summary>Documentation title.</summary>
    public string Title { get; set; } = "TextChunker API";
}
