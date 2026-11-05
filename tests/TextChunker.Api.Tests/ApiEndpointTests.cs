using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace TextChunker.Api.Tests;

public sealed class ApiEndpointTests(ApiServerFixture fixture) : IClassFixture<ApiServerFixture>
{
    private readonly ApiServerFixture _fixture = fixture;

    private ApiServer Server => _fixture.Server;

    [Fact]
    public async Task HealthReturnsOk()
    {
        using HttpResponseMessage response = await Server.Client.GetAsync("/api/v1/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("ok", body.GetProperty("status").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.GetProperty("version").GetString()));
    }

    [Fact]
    public async Task DictionariesListSupportedValues()
    {
        JsonElement tokenizers = JsonDocument
            .Parse(await Server.Client.GetStringAsync("/api/v1/tokenizers")).RootElement;
        Assert.Equal(
            new[] { "none", "char", "word", "regex", "huggingface" },
            tokenizers.GetProperty("names").EnumerateArray().Select(e => e.GetString()).ToArray());
        Assert.Equal("google/embeddinggemma-2", tokenizers.GetProperty("defaultModel").GetString());

        JsonElement strategies = JsonDocument
            .Parse(await Server.Client.GetStringAsync("/api/v1/strategies")).RootElement;
        Assert.Equal(
            new[] { "fixed", "recursive", "paragraph", "semantic" },
            strategies.GetProperty("names").EnumerateArray().Select(e => e.GetString()).ToArray());
    }

    [Fact]
    public async Task ChunkReturnsChunksAndStats()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync("/api/v1/chunk", new
        {
            text = "abcdefghij",
            strategy = "fixed",
            tokenizer = "char",
            maxChars = 4
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        JsonElement[] chunks = body.GetProperty("chunks").EnumerateArray().ToArray();

        Assert.Equal(3, chunks.Length);
        Assert.Equal("abcd", chunks[0].GetProperty("text").GetString());
        Assert.Equal(0, chunks[0].GetProperty("startChar").GetInt64());
        Assert.Equal(3, body.GetProperty("stats").GetProperty("chunkCount").GetInt32());
        Assert.All(chunks, chunk => Assert.True(chunk.GetProperty("text").GetString()!.Length <= 4));
    }

    [Fact]
    public async Task SourceAndMetadataAreApplied()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync("/api/v1/chunk", new
        {
            text = "abcdef",
            strategy = "fixed",
            tokenizer = "char",
            maxChars = 3,
            source = "doc.md",
            metadata = new Dictionary<string, string> { ["lang"] = "ru" }
        });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        JsonElement chunk = body.GetProperty("chunks")[0];

        Assert.Equal("doc.md", chunk.GetProperty("source").GetString());
        Assert.StartsWith("doc.md:", chunk.GetProperty("id").GetString());
        Assert.Equal("ru", chunk.GetProperty("metadata").GetProperty("lang").GetString());
    }

    [Fact]
    public async Task StreamReturnsNdjson()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/chunk/stream")
        {
            Content = JsonContent.Create(new
            {
                text = "abcdefghij",
                strategy = "fixed",
                tokenizer = "char",
                maxChars = 4
            })
        };

        using HttpResponseMessage response = await Server.Client.SendAsync(
            request, HttpCompletionOption.ResponseHeadersRead);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("application/x-ndjson", response.Content.Headers.ContentType?.MediaType);

        string payload = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("\r", payload);
        string[] lines = payload.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(3, lines.Length);
        Assert.All(lines, line => JsonDocument.Parse(line));
    }

    [Fact]
    public async Task MissingTextReturns400()
    {
        using HttpResponseMessage response = await Server.Client.PostAsJsonAsync("/api/v1/chunk", new { text = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UnknownStrategyAndTokenizerReturn400()
    {
        using HttpResponseMessage badStrategy = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk", new { text = "abc", strategy = "bogus" });
        Assert.Equal(HttpStatusCode.BadRequest, badStrategy.StatusCode);

        using HttpResponseMessage badTokenizer = await Server.Client.PostAsJsonAsync(
            "/api/v1/chunk", new { text = "abc", tokenizer = "bogus" });
        Assert.Equal(HttpStatusCode.BadRequest, badTokenizer.StatusCode);
    }

    [Fact]
    public async Task InvalidJsonReturns400()
    {
        var content = new StringContent("{ this is not json", Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk", content);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task BadTokenizerFileReturns503()
    {
        string path = Path.Combine(Path.GetTempPath(), $"textchunker-api-bad-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, "{ not valid json");
        try
        {
            using HttpResponseMessage response = await Server.Client.PostAsJsonAsync("/api/v1/chunk", new
            {
                text = "abc",
                tokenizer = "huggingface",
                tokenizerPath = path
            });

            Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task TooLongTextReturns413()
    {
        await using ApiServer server = await ApiServer.StartAsync(new Dictionary<string, string>
        {
            ["Chunking__MaxTextChars"] = "10"
        });

        using HttpResponseMessage response = await server.Client.PostAsJsonAsync(
            "/api/v1/chunk", new { text = new string('a', 11) });

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    private static MultipartFormDataContent CreateFileForm(
        string fileName,
        byte[] content,
        params (string Key, string Value)[] fields)
    {
        var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        form.Add(file, "file", fileName);
        foreach ((string key, string value) in fields)
        {
            form.Add(new StringContent(value), key);
        }
        return form;
    }

    [Fact]
    public async Task UploadTxtFileReturnsChunks()
    {
        using MultipartFormDataContent form = CreateFileForm(
            "sample.txt",
            Encoding.UTF8.GetBytes("abcdefghij"),
            ("strategy", "fixed"),
            ("tokenizer", "char"),
            ("maxChars", "4"));

        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        JsonElement[] chunks = body.GetProperty("chunks").EnumerateArray().ToArray();
        Assert.Equal(3, chunks.Length);
        Assert.Equal("abcd", chunks[0].GetProperty("text").GetString());
        Assert.Equal("api", chunks[0].GetProperty("source").GetString());
    }

    [Fact]
    public async Task UploadMarkdownAppliesOptionsMetadataAndSource()
    {
        using MultipartFormDataContent form = CreateFileForm(
            "doc.md",
            Encoding.UTF8.GetBytes("# Title\n\nabcdef"),
            ("strategy", "fixed"),
            ("tokenizer", "char"),
            ("maxChars", "5"),
            ("source", "doc.md"),
            ("metadata", "{\"lang\":\"ru\"}"),
            ("separators", "[\"\\n\\n\",\"\\n\"]"));

        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        JsonElement chunk = body.GetProperty("chunks")[0];
        Assert.Equal("doc.md", chunk.GetProperty("source").GetString());
        Assert.StartsWith("doc.md:", chunk.GetProperty("id").GetString());
        Assert.Equal("ru", chunk.GetProperty("metadata").GetProperty("lang").GetString());
    }

    [Fact]
    public async Task UploadUnsupportedExtensionReturns415()
    {
        using MultipartFormDataContent form = CreateFileForm(
            "doc.pdf", Encoding.UTF8.GetBytes("abc"));

        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task UploadEmptyFileReturns400()
    {
        using MultipartFormDataContent form = CreateFileForm(
            "empty.txt", Array.Empty<byte>());

        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadWithoutFilePartReturns400()
    {
        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("fixed"), "strategy");

        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UploadTooLongFileReturns413()
    {
        await using ApiServer server = await ApiServer.StartAsync(new Dictionary<string, string>
        {
            ["Chunking__MaxTextChars"] = "10"
        });

        using MultipartFormDataContent form = CreateFileForm(
            "big.txt", Encoding.UTF8.GetBytes(new string('a', 11)));

        using HttpResponseMessage response = await server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, response.StatusCode);
    }

    [Fact]
    public async Task UploadWithWindows1251EncodingIsDecoded()
    {
        // ASCII-байты одинаковы в cp1251; проверяем, что кодировка принимается и файл читается.
        using MultipartFormDataContent form = CreateFileForm(
            "cp.txt",
            Encoding.ASCII.GetBytes("hello world"),
            ("encoding", "windows-1251"),
            ("tokenizer", "word"),
            ("maxWords", "1"));

        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        JsonElement body = JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement;
        Assert.Equal("hello", body.GetProperty("chunks")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task UploadStrictInvalidUtf8Returns400()
    {
        using MultipartFormDataContent form = CreateFileForm(
            "bad.txt",
            new byte[] { 0x41, 0x80, 0x42 },
            ("encoding", "utf-8"),
            ("invalidEncoding", "error"));

        using HttpResponseMessage response = await Server.Client.PostAsync("/api/v1/chunk/file", form);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }
}
