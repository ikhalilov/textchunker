using System.Net;
using System.Net.Http.Json;

namespace TextChunker.Api.Tests;

/// <summary>Проверяет централизованную проверку X-API-Key для semantic-запросов.</summary>
public sealed class ApiKeyTests(ApiServerWithApiKeyFixture fixture) : IClassFixture<ApiServerWithApiKeyFixture>
{
    private readonly ApiServerWithApiKeyFixture _fixture = fixture;

    private ApiServer Server => _fixture.Server;

    private static object SemanticBody() => new
    {
        text = "One. Two. Three.",
        strategy = "semantic",
        tokenizer = "word",
        maxTokens = 20,
        embedding = new { provider = "hash", useCache = false }
    };

    [Fact]
    public async Task SemanticRequestWithoutApiKeyReturns401()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            SemanticBody());

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task SemanticRequestWithValidApiKeySucceeds()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chunk")
        {
            Content = JsonContent.Create(SemanticBody())
        };
        request.Headers.Add("X-API-Key", ApiServerWithApiKeyFixture.ApiKey);

        using HttpResponseMessage response = await Server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task SemanticStreamWithoutApiKeyReturns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chunk/stream")
        {
            Content = JsonContent.Create(SemanticBody())
        };

        using HttpResponseMessage response = await Server.Client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task SemanticFileUploadWithoutApiKeyReturns401()
    {
        using var form = new MultipartFormDataContent();
        form.Add(
            new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes("One. Two. Three.")),
            "file",
            "text.md");
        form.Add(new StringContent("semantic"), "strategy");
        form.Add(new StringContent("word"), "tokenizer");
        form.Add(new StringContent("20"), "maxTokens");
        form.Add(new StringContent("hash"), "embeddingProvider");
        form.Add(new StringContent("false"), "embeddingUseCache");

        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task NonSemanticRequestWithoutApiKeySucceeds()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk",
            new
            {
                text = "One. Two. Three.",
                strategy = "recursive",
                tokenizer = "word",
                maxTokens = 20
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }
}
