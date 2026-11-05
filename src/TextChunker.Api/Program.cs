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
    // Ограничение тела запроса; текст дополнительно проверяется на MaxTextChars.
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
    .WithTags("Справочники")
    .WithSummary("Состояние сервиса")
    .WithDescription("Возвращает `ok` и версию сборки. Используйте для health-check.")
    .Produces<HealthResponse>(StatusCodes.Status200OK);

api.MapGet("/tokenizers", () => Results.Ok(new TokenizersResponse(
    TokenizerFactory.SupportedNames,
    HuggingFaceTokenizer.DefaultModelName)))
    .WithName("Tokenizers")
    .WithTags("Справочники")
    .WithSummary("Список токенизаторов")
    .WithDescription("Допустимые значения поля `tokenizer` и модель HuggingFace по умолчанию.")
    .Produces<TokenizersResponse>(StatusCodes.Status200OK);

api.MapGet("/strategies", () => Results.Ok(new StrategiesResponse(
    ChunkerFactory.SupportedStrategies)))
    .WithName("Strategies")
    .WithTags("Справочники")
    .WithSummary("Список стратегий нарезки")
    .WithDescription("Допустимые значения поля `strategy`: `fixed`, `recursive`, `paragraph`, `semantic`.")
    .Produces<StrategiesResponse>(StatusCodes.Status200OK);

// Не-потоковая нарезка: весь текст в памяти, ответ — все чанки + статистика.
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
    .WithTags("Нарезка")
    .WithSummary("Нарезать текст на чанки")
    .WithDescription(
        "Режет `text` выбранной стратегией и возвращает все чанки с агрегированной статистикой. " +
        "Для `strategy=semantic` нужен доступный сервер эмбеддингов (llama.cpp) или `provider=hash`.")
    .Accepts<ChunkRequest>("application/json")
    .Produces<ChunkResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

// Загрузка файла (.txt/.md/.markdown) как multipart/form-data.
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
            logger.LogDebug(exception, "Ошибка валидации загрузки файла");
        }
        else
        {
            logger.LogError(exception, "Неожиданная ошибка загрузки файла");
        }

        return ApiErrors.ToResult(exception);
    }
})
    .AddEndpointFilter<SemanticApiKeyFilter>()
    .DisableAntiforgery()
    .WithName("ChunkFile")
    .WithTags("Нарезка")
    .WithSummary("Нарезать загруженный файл")
    .WithDescription(
        "Принимает `multipart/form-data`: часть `file` (`.txt`, `.md`, `.markdown`) и опции нарезки " +
        "как form-поля. Для `semantic` доступны плоские поля `embedding*` и `semantic*` " +
        "либо JSON-строки `embedding` / `semantic`.")
    .Produces<ChunkResponse>(StatusCodes.Status200OK)
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .ProducesProblem(StatusCodes.Status415UnsupportedMediaType)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

// Потоковая нарезка: application/x-ndjson, по одному чанку на строку.
api.MapPost("/chunk/stream", async (
    ChunkRequest request,
    ChunkingEngine engine,
    HttpContext http,
    ILogger<Program> logger) =>
{
    try
    {
        ChunkSession session = engine.CreateSession(request);

        // Semantic работает в document-mode: строим план и чанки до отправки заголовков,
        // чтобы ошибки провайдера/лимита вернулись как 503/413, а не пустой 200.
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
                    // Пробрасываем ошибку читателю через канал, иначе поток молча оборвётся.
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
                // Клиент отключился — штатное завершение.
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Ошибка записи потокового ответа");
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
                    logger.LogError(exception, "Ошибка нарезки в потоковом режиме");
                }

                try
                {
                    await text.FlushAsync(CancellationToken.None);
                }
                catch (Exception)
                {
                    // Ответ уже закрыт клиентом.
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
    .WithTags("Нарезка")
    .WithSummary("Потоковая нарезка (NDJSON)")
    .WithDescription(
        "Возвращает `application/x-ndjson`: по одному JSON-чанку на строку. " +
        "Для `strategy=semantic` чанки формируются до отправки заголовков, поэтому ошибки " +
        "провайдера и лимита приходят как 503/413, а не как оборванный поток.")
    .Accepts<ChunkRequest>("application/json")
    .Produces<Chunk>(StatusCodes.Status200OK, "application/x-ndjson")
    .ProducesProblem(StatusCodes.Status400BadRequest)
    .ProducesProblem(StatusCodes.Status413PayloadTooLarge)
    .ProducesProblem(StatusCodes.Status503ServiceUnavailable)
    .ProducesProblem(StatusCodes.Status500InternalServerError);

app.Run();
