using System.Net;
using System.Text;

using TextChunker.Tokenizers;

namespace TextChunker.Tests;

/// <summary>
/// Verification of the first-download path of the HF tokenizer: the file is downloaded once into the cache,
/// a repeated load runs offline (without HTTP), a download error → TokenizerLoadException (exit 5).
/// HTTP is replaced by a fake handler — the real network is not used.
/// </summary>
public sealed class HuggingFaceDownloadTests
{
    private const string MinimalTokenizerJson = """
    {
      "added_tokens": [],
      "model": {
        "type": "BPE",
        "vocab": { "<unk>": 0, "a": 1, "b": 2, "ab": 3, " ": 4 },
        "merges": [ ["a", "b"] ],
        "byte_fallback": false,
        "unk_token": "<unk>",
        "fuse_unk": false,
        "continuing_subword_prefix": null,
        "end_of_word_suffix": null
      }
    }
    """;

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly byte[] _payload;
        private readonly HttpStatusCode _status;
        private readonly bool _throw;

        public FakeHandler(byte[] payload, HttpStatusCode status = HttpStatusCode.OK, bool fail = false)
        {
            _payload = payload;
            _status = status;
            _throw = fail;
        }

        public int Calls { get; private set; }

        public Uri? LastRequestUri { get; private set; }

        protected override HttpResponseMessage Send(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Handle(request);
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Handle(request));
        }

        private HttpResponseMessage Handle(HttpRequestMessage request)
        {
            Calls++;
            LastRequestUri = request.RequestUri;
            if (_throw)
            {
                throw new InvalidOperationException("Сеть не должна использоваться: tokenizer.json уже в кэше.");
            }

            return new HttpResponseMessage(_status)
            {
                Content = new ByteArrayContent(_payload)
            };
        }
    }

    [Fact]
    public void FirstUseDownloadsIntoCacheAndSecondWorksOffline()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), $"textchunker-hf-{Guid.NewGuid():N}");
        byte[] payload = Encoding.UTF8.GetBytes(MinimalTokenizerJson);

        try
        {
            var downloadHandler = new FakeHandler(payload);
            var first = new HuggingFaceTokenizer("test/model", cacheDir, new HttpClient(downloadHandler));

            Assert.Equal(1, downloadHandler.Calls);
            Assert.NotNull(downloadHandler.LastRequestUri);
            Assert.Contains("test/model/resolve/main/tokenizer.json", downloadHandler.LastRequestUri!.ToString());
            Assert.Equal("test/model", first.ModelName);

            string cachedPath = Path.Combine(cacheDir, "test_model", "tokenizer.json");
            Assert.True(File.Exists(cachedPath));
            Assert.Equal(payload, File.ReadAllBytes(cachedPath));
            Assert.False(File.Exists(cachedPath + ".tmp"));

            int abCount = first.CountTokens("ab");
            Assert.Equal(1, abCount);
            Assert.Equal(2, first.CountTokens("a b"));

            var offlineHandler = new FakeHandler(Array.Empty<byte>(), fail: true);
            var second = new HuggingFaceTokenizer("test/model", cacheDir, new HttpClient(offlineHandler));

            Assert.Equal(0, offlineHandler.Calls);
            Assert.Equal(abCount, second.CountTokens("ab"));
            Assert.Equal(2, second.CountTokens("a b"));
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }

    [Fact]
    public void DownloadFailureThrowsTokenizerLoadExceptionWithExitCode5()
    {
        string cacheDir = Path.Combine(Path.GetTempPath(), $"textchunker-hf-{Guid.NewGuid():N}");
        var handler = new FakeHandler(Array.Empty<byte>(), HttpStatusCode.NotFound);

        try
        {
            var exception = Assert.Throws<TokenizerLoadException>(
                () => new HuggingFaceTokenizer("missing/model", cacheDir, new HttpClient(handler)));

            Assert.Equal(5, exception.ExitCode);
            Assert.Equal(1, handler.Calls);
            Assert.False(File.Exists(Path.Combine(cacheDir, "missing_model", "tokenizer.json")));
            Assert.False(File.Exists(Path.Combine(cacheDir, "missing_model", "tokenizer.json.tmp")));
        }
        finally
        {
            if (Directory.Exists(cacheDir))
            {
                Directory.Delete(cacheDir, recursive: true);
            }
        }
    }
}
