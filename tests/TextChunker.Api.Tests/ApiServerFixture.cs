namespace TextChunker.Api.Tests;

public sealed class ApiServerFixture : IAsyncLifetime
{
    public ApiServer Server { get; private set; } = null!;

    public async ValueTask InitializeAsync()
    {
        Server = await ApiServer.StartAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await Server.DisposeAsync();
    }
}
