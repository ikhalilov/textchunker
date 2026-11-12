using System.Reflection;
using System.Text;
using System.Threading.Channels;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TextChunker;
using TextChunker.Abstractions;
using TextChunker.Api.Contracts;
using TextChunker.Api.Services;
using TextChunker.Chunking;
using TextChunker.Tokenizers;
using TextChunker.Writers;

Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

const long MaxRequestBodySizeBytes = 20 * 1024 * 1024;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOptions<ApiOptions>()
    .Bind(builder.Configuration.GetSection("Chunking"))
    .ValidateDataAnnotations()
    .ValidateOnStart();
builder.Services.AddSingleton<TokenizerCache>();
builder.Services.AddSingleton<ChunkingEngine>();
builder.Services.AddSingleton<SemanticApiKeyFilter>();
builder.Services.AddProblemDetails();
builder.Services.AddApiDocs(builder.Configuration);

builder.WebHost.ConfigureKestrel(options =>
{
    // Request body limit; the text is additionally checked against MaxTextChars.
    options.Limits.MaxRequestBodySize = MaxRequestBodySizeBytes;
});

var app = builder.Build();

app.UseExceptionHandler();
app.MapApiDocs();

var api = app.MapGroup("/api/v1");

api.MapGet("/health", () => Results.Ok(new HealthResponse(
    "ok",
    typeof(Program).Assembly
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? "0.0.0")))
    .WithName("Health")
    .WithTags("Reference")
    .WithSummary("Service health")
    .WithDescription("Returns `ok` and the assembly version. Use it for health checks.")
    .Produces<HealthResponse>(StatusCodes.Status200OK);

api.MapGet("/tokenizers", () => Results.Ok(new TokenizersResponse(
    TokenizerFactory.SupportedNames,
    HuggingFaceTokenizer.DefaultModelName)))
    .WithName("Tokenizers")
    .WithTags("Reference")
    .WithSummary("List of tokenizers")
    .WithDescription("Allowed `tokenizer` values and the default HuggingFace model.")
    .Produces<TokenizersResponse>(StatusCodes.Status200OK);

api.MapGet("/strategies", () => Results.Ok(new StrategiesResponse(
    ChunkerFactory.SupportedStrategies)))
    .WithName("Strategies")
    .WithTags("Reference")
    .WithSummary("List of chunking strategies")
    .WithDescription("Allowed `strategy` values: `fixed`, `recursive`, `paragraph`, `semantic`.")
    .Produces<StrategiesResponse>(StatusCodes.Status200OK);

// Non-streaming chunking: the whole text is held in memory; the response contains all chunks + stats.
api.MapPost("/chunk", async (ChunkRequest request, ChunkingEngine engine, HttpContext http) =>
{
    try
    {
        ChunkResult result = await engine.RunAsync(request, http.RequestAborted);
        return Results.Ok(new ChunkResponse(result.Chunks, result.Stats));
    }
    catch (Exception exception)
    {
        return ApiErrors.ToResult(exception);
    }
})
    .AddEndpointFilter<SemanticApiKeyFilter>()
    .WithName("Chunk")
    .WithTags("Chunking")
    .WithSummary("Chunk text into pieces")
    .WithDescription(
        "Splits `text` with the selected strategy and returns all chunks with aggregated statistics. " +
        "For `strategy=semantic`, an available embedding server (llama.cpp) or `provider=hash` is required.")
    .Accepts<ChunkRequest>("application/json")
    .Produces<ChunkResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

// File upload (.txt/.md/.markdown) as multipart/form-data.
api.MapPost("/chunk/file", async (
    IFormFile? file,
    [FromForm] FileChunkForm? form,
    ChunkingEngine engine,
    IOptionsMonitor<ApiOptions> options,
    HttpContext http,
    ILogger<Program> logger) =>
{
    FileChunkForm effectiveForm = form ?? new FileChunkForm();
    try
    {
        string text = await FileUploadReader.ReadAsync(
            file,
            effectiveForm.Encoding,
            effectiveForm.InvalidEncoding,
            options.CurrentValue.MaxTextChars,
            http.RequestAborted);

        ChunkResult result = await engine.RunAsync(effectiveForm.ToRequest(text), http.RequestAborted);
        return Results.Ok(new ChunkResponse(result.Chunks, result.Stats));
    }
    catch (Exception exception)
    {
        if (exception is ArgumentException or PayloadTooLargeException or UnsupportedMediaTypeException or TokenizerLoadException or OperationCanceledException)
        {
            logger.LogDebug(exception, "File upload validation error");
        }
        else
        {
            logger.LogError(exception, "Unexpected file upload error");
        }

        return ApiErrors.ToResult(exception);
    }
})
    .AddEndpointFilter<SemanticApiKeyFilter>()
    .DisableAntiforgery()
    .WithName("ChunkFile")
    .WithTags("Chunking")
    .WithSummary("Chunk an uploaded file")
    .WithDescription(
        "Accepts `multipart/form-data`: a `file` part (`.txt`, `.md`, `.markdown`) and chunking options " +
        "as form fields. For `semantic`, the flat fields `embedding*` and `semantic*` are available, " +
        "or the JSON strings `embedding` / `semantic`.")
    .Produces<ChunkResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

// Streaming chunking: application/x-ndjson, one chunk per line.
api.MapPost("/chunk/stream", async (
    ChunkRequest request,
    ChunkingEngine engine,
    HttpContext http,
    ILogger<Program> logger) =>
{
    try
    {
        ChunkSession session = engine.CreateSession(request);

        // Semantic runs in document mode: build the plan and chunks before sending headers,
        // so provider/limit errors are returned as 503/413 instead of an empty 200.
        if (session.Service.RequiresPreplanning)
        {
            ChunkResult result = await engine.RunAsync(session, http.RequestAborted);
            return Results.Stream(async output =>
            {
                var text = new StreamWriter(
                    output,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                    4096,
                    leaveOpen: true)
                {
                    AutoFlush = false,
                    NewLine = "\n"
                };

                foreach (Chunk chunk in result.Chunks)
                {
                    await text.WriteLineAsync(
                        ChunkJson.Serialize(chunk).AsMemory(),
                        http.RequestAborted);
                }
                await text.FlushAsync(http.RequestAborted);
            }, "application/x-ndjson; charset=utf-8");
        }

        return Results.Stream(async output =>
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(http.RequestAborted);
            CancellationToken token = cts.Token;

            var channel = Channel.CreateBounded<Chunk>(new BoundedChannelOptions(64)
            {
                SingleReader = true,
                SingleWriter = true,
                FullMode = BoundedChannelFullMode.Wait
            });

            Task producer = Task.Run(() =>
            {
                try
                {
                    session.Service.Process(
                        session.Source,
                        new ChannelChunkWriter(channel.Writer, token),
                        token);
                    channel.Writer.TryComplete();
                }
                catch (Exception exception)
                {
                    // Propagate the error to the reader through the channel; otherwise the stream ends silently.
                    channel.Writer.TryComplete(exception);
                }
            }, CancellationToken.None);

            var text = new StreamWriter(
                output,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                4096,
                leaveOpen: true)
            {
                AutoFlush = false,
                NewLine = "\n"
            };

            try
            {
                await foreach (Chunk chunk in channel.Reader.ReadAllAsync(token))
                {
                    await text.WriteLineAsync(ChunkJson.Serialize(chunk).AsMemory(), token);
                    await text.FlushAsync(token);
                }
            }
            catch (OperationCanceledException)
            {
                // The client disconnected — normal termination.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Streaming response write error");
            }
            finally
            {
                cts.Cancel();

                try
                {
                    await producer;
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception exception)
                {
                    logger.LogError(exception, "Streaming chunking error");
                }

                try
                {
                    await text.FlushAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    // The response is already closed by the client.
                }
            }
        }, "application/x-ndjson; charset=utf-8");
    }
    catch (Exception exception)
    {
        return ApiErrors.ToResult(exception);
    }
})
    .AddEndpointFilter<SemanticApiKeyFilter>()
    .WithName("ChunkStream")
    .WithTags("Chunking")
    .WithSummary("Streaming chunking (NDJSON)")
    .WithDescription(
        "Returns `application/x-ndjson`: one JSON chunk per line. " +
        "For `strategy=semantic`, chunks are built before the headers are sent, so provider " +
        "and limit errors arrive as 503/413 rather than a broken stream.")
    .Accepts<ChunkRequest>("application/json")
    .Produces<Chunk>(StatusCodes.Status200OK, "application/x-ndjson")
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.Run();
