using System.Net;
using System.Text.Json;

namespace TextChunker.Api.Tests;

/// <summary>Отключение и настройка документации через конфигурацию.</summary>
public sealed class ApiDocsConfigurationTests
{
    [Fact]
    public async Task DocsCanBeDisabled()
    {
        await using ApiServer server = await ApiServer.StartAsync(
            new Dictionary<string, string> { ["ApiDocs__Enabled"] = "false" });

        using HttpResponseMessage openApi = await server.Client.GetAsync("/openapi/v1.json");
        using HttpResponseMessage scalar = await server.Client.GetAsync("/scalar");
        using HttpResponseMessage health = await server.Client.GetAsync("/api/v1/health");

        Assert.Equal(HttpStatusCode.NotFound, openApi.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, scalar.StatusCode);
        Assert.Equal(HttpStatusCode.OK, health.StatusCode);
    }

    [Fact]
    public async Task ScalarRouteIsConfigurable()
    {
        await using ApiServer server = await ApiServer.StartAsync(
            new Dictionary<string, string> { ["ApiDocs__Route"] = "/docs" });

        using HttpResponseMessage docs = await server.Client.GetAsync("/docs");
        using HttpResponseMessage defaultRoute = await server.Client.GetAsync("/scalar");

        Assert.Equal(HttpStatusCode.OK, docs.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, defaultRoute.StatusCode);
    }

    [Fact]
    public async Task TitleIsConfigurable()
    {
        await using ApiServer server = await ApiServer.StartAsync(
            new Dictionary<string, string> { ["ApiDocs__Title"] = "Custom API" });

        using HttpResponseMessage response = await server.Client.GetAsync("/openapi/v1.json");
        JsonElement document = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal("Custom API", document.GetProperty("info").GetProperty("title").GetString());
    }
}
