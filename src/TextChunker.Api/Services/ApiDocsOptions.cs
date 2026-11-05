namespace TextChunker.Api.Services;

/// <summary>
/// Настройки интерактивной документации API (секция <c>ApiDocs</c>):
/// OpenAPI-документ и UI Scalar.
/// </summary>
public sealed class ApiDocsOptions
{
    /// <summary>true — публиковать OpenAPI и Scalar (по умолчанию включено).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Маршрут Scalar UI.</summary>
    public string Route { get; set; } = "/scalar";

    /// <summary>Маршрут OpenAPI-документа; обязан содержать <c>{documentName}</c>.</summary>
    public string OpenApiRoute { get; set; } = "/openapi/{documentName}.json";

    /// <summary>Заголовок документации.</summary>
    public string Title { get; set; } = "TextChunker API";
}
