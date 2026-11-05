namespace TextChunker.Api.Tests;

public sealed class ApiServerWithApiKeyFixture : IAsyncLifetime
{
    public const string ApiKey = "test-api-key";

    public ApiServer Server { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Server = await ApiServer.StartAsync(new Dictionary<string, string>
        {
            ["TEXTCHUNKER_EMBEDDING_API_KEY"] = ApiKey
        });
    }

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
    }
}
