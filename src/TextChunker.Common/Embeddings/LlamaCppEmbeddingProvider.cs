using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;

using TextChunker.Abstractions.Embeddings;

namespace TextChunker.Embeddings;

/// <summary>
/// Провайдер эмбеддингов поверх внешнего llama.cpp-сервера:
/// <c>POST {endpoint}/v1/embeddings</c> (OpenAI-совместимый протокол) с телом
/// <c>{ "model": "...", "input": [ ... ] }</c>. Батчнирует, переотправляет сетевые
/// ошибки и 429/5xx с экспоненциальной задержкой, проверяет размерность и
/// L2-нормализует векторы. Сам сервер приложением не запускается.
/// </summary>
public sealed class LlamaCppEmbeddingProvider : IEmbeddingProvider, IEmbeddingModelInfo
{
    private const string EmbeddingsSuffix = "/v1/embeddings";

    private readonly EmbeddingOptions _options;
    private readonly string _embeddingsUrl;
    private readonly HttpClient _http;
    private readonly string? _apiKey;

    public string Name => "llama-cpp";

    public int Dimensions => _options.Dimensions;

    /// <summary>Значение поля <c>model</c> в запросе (null — поле не отправляется).</summary>
    public string? Model => string.IsNullOrWhiteSpace(_options.Model) ? null : _options.Model!.Trim();

    string? IEmbeddingModelInfo.ModelName => Model;

    /// <summary>Полный URL эндпоинта эмбеддингов (для диагностики и тестов).</summary>
    internal string EmbeddingsUrlForTests => _embeddingsUrl;

    public LlamaCppEmbeddingProvider(EmbeddingOptions options)
        : this(options, options?.ApiKey, httpClient: null)
    {
    }

    public LlamaCppEmbeddingProvider(EmbeddingOptions options, string? apiKey)
        : this(options, apiKey, httpClient: null)
    {
    }

    /// <summary>Тестируемая перегрузка: позволяет подменить <see cref="HttpClient"/>.</summary>
    public LlamaCppEmbeddingProvider(EmbeddingOptions options, string? apiKey, HttpClient? httpClient)
    {
        _options = options ?? throw new ArgumentNullException(nameof(options));
        _options.Validate();
        _apiKey = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey.Trim();

        // Абсолютный URL: не зависим от BaseAddress注入-клиента.
        _embeddingsUrl = options.Endpoint.TrimEnd('/') + EmbeddingsSuffix;

        // Провайдер живёт столько же, сколько процесс (как и кэш токенизаторов),
        // поэтому HttpClient создаётся один раз и не освобождается явно.
        _http = httpClient ?? new HttpClient { Timeout = options.Timeout };
    }

    public async Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts is null)
        {
            throw new ArgumentNullException(nameof(texts));
        }
        if (texts.Count == 0)
        {
            return Array.Empty<float[]>();
        }

        // Дедупликация: одинаковые тексты (частый случай в повторяющихся документах)
        // эмбеддятся один раз. Это и меньше запросов, и стабильный результат между
        // холодным прогоном и прогоном из кэша.
        var uniqueTexts = new List<string>(texts.Count);
        var uniqueIndex = new Dictionary<string, int>(texts.Count, StringComparer.Ordinal);
        var mapping = new int[texts.Count];
        for (int i = 0; i < texts.Count; i++)
        {
            string prepared = WithPrompt(texts[i]);
            if (!uniqueIndex.TryGetValue(prepared, out int index))
            {
                index = uniqueTexts.Count;
                uniqueIndex.Add(prepared, index);
                uniqueTexts.Add(prepared);
            }
            mapping[i] = index;
        }

        float[][] uniqueVectors = await EmbedPreparedAsync(uniqueTexts, cancellationToken).ConfigureAwait(false);
        var result = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
        {
            result[i] = uniqueVectors[mapping[i]];
        }

        return result;
    }

    private async Task<float[][]> EmbedPreparedAsync(
        IReadOnlyList<string> preparedTexts,
        CancellationToken cancellationToken)
    {
        var result = new float[preparedTexts.Count][];
        List<(int Offset, List<string> Texts)> batches = BuildBatches(preparedTexts);
        if (_options.Concurrency <= 1 || batches.Count == 1)
        {
            foreach ((int offset, List<string> batch) in batches)
            {
                float[][] vectors = await EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false);
                for (int i = 0; i < vectors.Length; i++)
                {
                    result[offset + i] = vectors[i];
                }
            }

            return result;
        }

        // Параллельные батчи: порядок результатов сохраняется по offset,
        // число одновременных запросов ограничено Concurrency.
        var gate = new SemaphoreSlim(_options.Concurrency, _options.Concurrency);
        var tasks = new List<Task>(batches.Count);
        foreach ((int offset, List<string> batch) in batches)
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            tasks.Add(EmbedBatchIntoAsync(offset, batch, result, gate, cancellationToken));
        }

        await Task.WhenAll(tasks).ConfigureAwait(false);
        return result;
    }

    /// <summary>
    /// Батчи по BatchSize и MaxBatchChars. Бюджет по символам — консервативная
    /// оценка токенов: провайдер не знает токенизатор сервера. Один текст длиннее
    /// бюджета всё равно отправляется отдельным батчем.
    /// </summary>
    private List<(int Offset, List<string> Texts)> BuildBatches(IReadOnlyList<string> texts)
    {
        var batches = new List<(int Offset, List<string> Texts)>();
        int offset = 0;
        while (offset < texts.Count)
        {
            var batch = new List<string>();
            int chars = 0;
            while (batch.Count < _options.BatchSize
                   && offset + batch.Count < texts.Count)
            {
                string text = texts[offset + batch.Count];
                if (batch.Count > 0
                    && _options.MaxBatchChars > 0
                    && chars + text.Length > _options.MaxBatchChars)
                {
                    break;
                }

                batch.Add(text);
                chars += text.Length;
            }

            batches.Add((offset, batch));
            offset += batch.Count;
        }

        return batches;
    }

    private async Task EmbedBatchIntoAsync(
        int offset,
        IReadOnlyList<string> batch,
        float[][] result,
        SemaphoreSlim gate,
        CancellationToken cancellationToken)
    {
        try
        {
            float[][] vectors = await EmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            for (int i = 0; i < vectors.Length; i++)
            {
                result[offset + i] = vectors[i];
            }
        }
        finally
        {
            gate.Release();
        }
    }

    private string WithPrompt(string text)
    {
        return string.IsNullOrEmpty(_options.Prompt) ? text : _options.Prompt + text;
    }

    /// <summary>
    /// Сервер не принял порцию целиком. Сначала делим батч пополам; если не влезает
    /// один текст — режем его по границе слова и усредняем векторы половин
    /// (взвешенно по длине, затем L2-нормализация).
    /// </summary>
    private async Task<float[][]> SplitAndEmbedAsync(
        IReadOnlyList<string> batch,
        CancellationToken cancellationToken)
    {
        if (batch.Count > 1)
        {
            int middle = batch.Count / 2;
            var left = new List<string>(middle);
            var right = new List<string>(batch.Count - middle);
            for (int i = 0; i < middle; i++)
            {
                left.Add(batch[i]);
            }
            for (int i = middle; i < batch.Count; i++)
            {
                right.Add(batch[i]);
            }

            float[][] leftVectors = await EmbedBatchAsync(left, cancellationToken).ConfigureAwait(false);
            float[][] rightVectors = await EmbedBatchAsync(right, cancellationToken).ConfigureAwait(false);

            var combined = new float[batch.Count][];
            leftVectors.CopyTo(combined, 0);
            rightVectors.CopyTo(combined, middle);
            return combined;
        }

        string text = batch[0];
        if (text.Length < 64)
        {
            throw new EmbeddingProviderException(
                $"llama.cpp не может обработать вход длиной {text.Length} символов. " +
                "Увеличьте физический батч сервера (-b/--batch-size).");
        }

        int cut = FindSplitPoint(text);
        string first = text.Substring(0, cut);
        string second = text.Substring(cut);
        float[][] firstVector = await EmbedBatchAsync(new[] { first }, cancellationToken).ConfigureAwait(false);
        float[][] secondVector = await EmbedBatchAsync(new[] { second }, cancellationToken).ConfigureAwait(false);
        return new[] { WeightedAverage(firstVector[0], first.Length, secondVector[0], second.Length) };
    }

    private static int FindSplitPoint(string text)
    {
        int middle = text.Length / 2;
        int cut = middle;
        while (cut > 0 && !char.IsWhiteSpace(text[cut - 1]))
        {
            cut--;
        }

        if (cut <= 0)
        {
            cut = middle;
        }

        // Не разрываем суррогатную пару.
        if (cut > 0 && cut < text.Length && char.IsLowSurrogate(text[cut]))
        {
            cut--;
        }

        return cut <= 0 || cut >= text.Length ? middle : cut;
    }

    private static float[] WeightedAverage(float[] left, int leftWeight, float[] right, int rightWeight)
    {
        int length = Math.Min(left.Length, right.Length);
        var result = new float[length];
        double total = leftWeight + rightWeight;
        if (total <= 0)
        {
            total = 1;
        }

        for (int i = 0; i < length; i++)
        {
            result[i] = (float)((left[i] * (double)leftWeight + right[i] * (double)rightWeight) / total);
        }

        return HashEmbeddingProvider.Normalize(result);
    }

    private static bool IsInputTooLarge(HttpStatusCode statusCode, string detail)
    {
        if ((int)statusCode < 400)
        {
            return false;
        }

        return detail.Contains("too large to process", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("input is too large", StringComparison.OrdinalIgnoreCase)
            || detail.Contains("exceeds the available context", StringComparison.OrdinalIgnoreCase);
    }

    private async Task<float[][]> EmbedBatchAsync(IReadOnlyList<string> batch, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            try
            {
                return await TryEmbedBatchAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (InputTooLargeException)
            {
                // Физический батч сервера меньше, чем наша порция: дробим батч/текст.
                return await SplitAndEmbedAsync(batch, cancellationToken).ConfigureAwait(false);
            }
            catch (Exception exception) when (IsTransient(exception, cancellationToken) && attempt < _options.MaxRetries)
            {
                await Task.Delay(Backoff(attempt), cancellationToken).ConfigureAwait(false);
            }
            catch (EmbeddingProviderException)
            {
                throw;
            }
            catch (Exception exception)
            {
                throw new EmbeddingProviderException(
                    $"Не удалось получить эмбеддинги с {_embeddingsUrl}: {exception.Message}",
                    exception);
            }
        }
    }

    private async Task<float[][]> TryEmbedBatchAsync(IReadOnlyList<string> batch, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, _embeddingsUrl);
        request.Content = CreateContent(batch);
        if (_apiKey is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
        }

        using HttpResponseMessage response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string detail = await SafeReadAsync(response, cancellationToken).ConfigureAwait(false);
            if (IsInputTooLarge(response.StatusCode, detail))
            {
                throw new InputTooLargeException(Truncate(detail));
            }

            var error = new EmbeddingProviderException(
                $"llama.cpp вернул HTTP {(int)response.StatusCode} для {_embeddingsUrl}: {Truncate(detail)}");

            if (IsRetryableStatus(response.StatusCode))
            {
                throw new TransientStatusException(error.Message, error);
            }
            throw error;
        }

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return ParseEmbeddings(json, batch.Count);
    }

    private HttpContent CreateContent(IReadOnlyList<string> batch)
    {
        var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            if (Model is not null)
            {
                writer.WriteString("model", Model);
            }
            writer.WritePropertyName("input");
            writer.WriteStartArray();
            foreach (string text in batch)
            {
                writer.WriteStringValue(text);
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        stream.Position = 0;

        var content = new StreamContent(stream);
        content.Headers.ContentType = new MediaTypeHeaderValue("application/json") { CharSet = "utf-8" };
        return content;
    }

    private float[][] ParseEmbeddings(string json, int expectedCount)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(json);
        }
        catch (JsonException exception)
        {
            throw new EmbeddingProviderException(
                $"llama.cpp вернул невалидный JSON: {exception.Message}", exception);
        }

        using (document)
        {
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !root.TryGetProperty("data", out JsonElement data)
                || data.ValueKind != JsonValueKind.Array)
            {
                throw new EmbeddingProviderException("В ответе эмбеддингов нет массива 'data'.");
            }

            var items = new List<(int Index, float[] Vector)>();
            int position = 0;
            foreach (JsonElement item in data.EnumerateArray())
            {
                if (item.ValueKind != JsonValueKind.Object
                    || !item.TryGetProperty("embedding", out JsonElement embedding)
                    || embedding.ValueKind != JsonValueKind.Array)
                {
                    throw new EmbeddingProviderException($"Элемент 'data[{position}]' не содержит массив 'embedding'.");
                }

                int index = position;
                if (item.TryGetProperty("index", out JsonElement indexElement))
                {
                    if (indexElement.ValueKind != JsonValueKind.Number)
                    {
                        throw new EmbeddingProviderException($"Поле 'index' элемента 'data[{position}]' не является числом.");
                    }
                    index = indexElement.GetInt32();
                }

                items.Add((index, ReadVector(embedding, position)));
                position++;
            }

            if (items.Count != expectedCount)
            {
                throw new EmbeddingProviderException(
                    $"Ожидалось {expectedCount} эмбеддингов, получено {items.Count}.");
            }

            var ordered = items.OrderBy(item => item.Index).ToList();
            var vectors = new float[ordered.Count][];
            for (int i = 0; i < ordered.Count; i++)
            {
                if (ordered[i].Index != i)
                {
                    throw new EmbeddingProviderException(
                        $"Индексы эмбеддингов не покрывают 0..{ordered.Count - 1} без пропусков (получен {ordered[i].Index}).");
                }
                vectors[i] = Validate(ordered[i].Vector, i);
            }

            return vectors;
        }
    }

    private float[] ReadVector(JsonElement embedding, int position)
    {
        var values = new List<float>();
        foreach (JsonElement value in embedding.EnumerateArray())
        {
            if (value.ValueKind != JsonValueKind.Number)
            {
                throw new EmbeddingProviderException($"Эмбеддинг 'data[{position}]' содержит не-число.");
            }
            values.Add(value.GetSingle());
        }

        if (values.Count == 0)
        {
            throw new EmbeddingProviderException($"Эмбеддинг 'data[{position}]' пуст.");
        }
        return values.ToArray();
    }

    private float[] Validate(float[] vector, int position)
    {
        if (_options.Dimensions > 0 && vector.Length != _options.Dimensions)
        {
            throw new EmbeddingProviderException(
                $"Эмбеддинг #{position} имеет размерность {vector.Length}, ожидалось {_options.Dimensions}.");
        }
        return HashEmbeddingProvider.Normalize(vector);
    }

    private static bool IsRetryableStatus(HttpStatusCode statusCode)
    {
        return statusCode == HttpStatusCode.TooManyRequests
            || (int)statusCode >= 500
            || statusCode == HttpStatusCode.RequestTimeout
            || statusCode == HttpStatusCode.BadGateway
            || statusCode == HttpStatusCode.ServiceUnavailable
            || statusCode == HttpStatusCode.GatewayTimeout;
    }

    private static bool IsTransient(Exception exception, CancellationToken cancellationToken)
    {
        return exception is TransientStatusException
            || exception is HttpRequestException
            || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested);
    }

    private static TimeSpan Backoff(int attempt)
    {
        double milliseconds = 200 * Math.Pow(2, attempt);
        return TimeSpan.FromMilliseconds(Math.Min(milliseconds, 5_000));
    }

    private static async Task<string> SafeReadAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception)
        {
            return "<тело недоступно>";
        }
    }

    private static string Truncate(string value)
    {
        return value.Length <= 500 ? value : value.Substring(0, 500) + "…";
    }

    /// <summary>Внутренний маркер «статус переотправляемый».</summary>
    private sealed class TransientStatusException : Exception
    {
        public TransientStatusException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }

    /// <summary>Вход не помещается в физический батч сервера; батч/текст нужно разделить.</summary>
    private sealed class InputTooLargeException : Exception
    {
        public InputTooLargeException(string message)
            : base(message)
        {
        }
    }
}
