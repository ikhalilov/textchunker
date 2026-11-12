using System.Net;
using System.Text.Json;

namespace TextChunker.Api.Tests;

/// <summary>OpenAPI document and Scalar UI.</summary>
public sealed class ApiDocsTests(ApiServerFixture fixture) : IClassFixture<ApiServerFixture>{
    private readonly ApiServerFixture _fixture = fixture;

    private ApiServer Server => _fixture.Server;

    private async Task<JsonElement> GetOpenApiAsync()
    {
        using HttpResponseMessage response = await Server.Client.GetAsync("/openapi/v1.json");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/json", response.Content.Headers.ContentType?.MediaType);
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }

    [Fact]
    public async Task OpenApiDocumentDescribesAllEndpoints()
    {
        JsonElement document = await GetOpenApiAsync();

        Assert.StartsWith("3.", document.GetProperty("openapi").GetString());
        Assert.Equal("TextChunker API", document.GetProperty("info").GetProperty("title").GetString());

        JsonElement paths = document.GetProperty("paths");
        foreach (string path in new[]
        {
            "/api/v1/health", "/api/v1/tokenizers", "/api/v1/strategies",
            "/api/v1/chunk", "/api/v1/chunk/file", "/api/v1/chunk/stream"
        })
        {
            Assert.True(paths.TryGetProperty(path, out _), $"в OpenAPI нет {path}");
        }

        // The service redirect of the root must not appear in the description.
        Assert.False(paths.TryGetProperty("/", out _));
    }

    [Fact]
    public async Task VersionHasNoBuildMetadata()
    {
        JsonElement document = await GetOpenApiAsync();

        string version = document.GetProperty("info").GetProperty("version").GetString()!;
        Assert.DoesNotContain("+", version, StringComparison.Ordinal);
        Assert.Matches(@"^\d+\.\d+\.\d+", version);
    }

    [Fact]
    public async Task ChunkOperationsHaveSummariesTagsAndProblemResponses()
    {
        JsonElement document = await GetOpenApiAsync();
        JsonElement chunk = document.GetProperty("paths").GetProperty("/api/v1/chunk").GetProperty("post");

        Assert.False(string.IsNullOrWhiteSpace(chunk.GetProperty("summary").GetString()));
        Assert.Equal(1, chunk.GetProperty("tags").GetArrayLength());

        JsonElement responses = chunk.GetProperty("responses");
        Assert.True(responses.TryGetProperty("200", out _));
        foreach (string code in new[] { "400", "413", "503" })
        {
            JsonElement problem = responses.GetProperty(code).GetProperty("content");
            Assert.True(
                problem.TryGetProperty("application/problem+json", out _),
                $"{code} должен описываться как problem+json");
        }
    }

    [Fact]
    public async Task StreamEndpointIsDescribedAsNdjson()
    {
        JsonElement document = await GetOpenApiAsync();
        JsonElement stream = document.GetProperty("paths").GetProperty("/api/v1/chunk/stream").GetProperty("post");

        JsonElement ok = stream.GetProperty("responses").GetProperty("200").GetProperty("content");
        Assert.True(ok.TryGetProperty("application/x-ndjson", out _));
    }

    [Fact]
    public async Task FileEndpointIsDescribedAsMultipartWithFilePart()
    {
        JsonElement document = await GetOpenApiAsync();
        JsonElement content = document.GetProperty("paths").GetProperty("/api/v1/chunk/file")
            .GetProperty("post").GetProperty("requestBody").GetProperty("content");

        Assert.True(content.TryGetProperty("multipart/form-data", out JsonElement multipart));
        Assert.Contains("\"file\"", multipart.GetRawText(), StringComparison.Ordinal);

        // The built-in OpenAPI generator references the file part as
        // #/components/schemas/IFormFile, where it is described as a binary string.
        JsonElement formFile = document.GetProperty("components").GetProperty("schemas")
            .GetProperty("IFormFile");
        Assert.Equal("string", formFile.GetProperty("type").GetString());
        Assert.Equal("binary", formFile.GetProperty("format").GetString());
    }

    [Fact]
    public async Task SchemasUseXmlDocumentationAndIncludeSemanticOptions()
    {
        JsonElement document = await GetOpenApiAsync();
        JsonElement schemas = document.GetProperty("components").GetProperty("schemas");

        JsonElement request = schemas.GetProperty("ChunkRequest").GetProperty("properties");
        string strategy = request.GetProperty("strategy").GetProperty("description").GetString()!;
        Assert.Contains("semantic", strategy, StringComparison.Ordinal);
        Assert.True(request.TryGetProperty("embedding", out _));
        Assert.True(request.TryGetProperty("semantic", out _));

        JsonElement embedding = schemas.GetProperty("EmbeddingRequest").GetProperty("properties");
        Assert.True(embedding.TryGetProperty("concurrency", out JsonElement concurrency));
        Assert.True(concurrency.TryGetProperty("description", out _));

        JsonElement chunk = schemas.GetProperty("Chunk").GetProperty("properties");
        Assert.True(chunk.GetProperty("startChar").TryGetProperty("description", out _));
    }

    [Fact]
    public async Task ReferenceEndpointsHaveTypedResponses()
    {
        JsonElement document = await GetOpenApiAsync();
        JsonElement schemas = document.GetProperty("components").GetProperty("schemas");

        Assert.True(schemas.TryGetProperty("HealthResponse", out _));
        Assert.True(schemas.TryGetProperty("TokenizersResponse", out _));
        Assert.True(schemas.TryGetProperty("StrategiesResponse", out _));
    }

    [Fact]
    public async Task ScalarUiIsServedAsHtml()
    {
        using HttpResponseMessage response = await Server.Client.GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType?.MediaType);
        string html = await response.Content.ReadAsStringAsync();
        Assert.Contains("scalar", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RootRedirectsToScalar()
    {
        using var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            BaseAddress = new Uri(Server.BaseUrl)
        };

        using HttpResponseMessage response = await client.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/scalar", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ExistingEndpointsKeepTheirJsonShape()
    {
        JsonElement health = JsonDocument
            .Parse(await Server.Client.GetStringAsync("/api/v1/health")).RootElement;
        Assert.Equal("ok", health.GetProperty("status").GetString());
        Assert.True(health.TryGetProperty("version", out _));

        JsonElement tokenizers = JsonDocument
            .Parse(await Server.Client.GetStringAsync("/api/v1/tokenizers")).RootElement;
        Assert.True(tokenizers.GetProperty("names").GetArrayLength() > 0);
        Assert.False(string.IsNullOrWhiteSpace(tokenizers.GetProperty("defaultModel").GetString()));
    }
}
