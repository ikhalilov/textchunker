# HTTP API (Minimal APIs)

Проект `src/TextChunker.Api` на ASP.NET Core Minimal APIs (net10.0). Использует тот же
движок нарезки (`ChunkingService`, стратегии, токенизаторы), что и CLI.

## Запуск

```bash
dotnet run --project src/TextChunker.Api --urls http://localhost:5080
```

Публикация и запуск:

```bash
dotnet publish src/TextChunker.Api -c Release -o out/api
./out/api/TextChunker.Api --urls http://0.0.0.0:5080
```

Конфигурация (`appsettings.json` / переменные окружения):

| Ключ | По умолчанию | Назначение |
|---|---|---|
| `Chunking:MaxTextChars` | `5 000 000` | максимальная длина `text` (иначе 413) |
| `Chunking:TokenizerCacheDir` | путь по умолчанию | каталог кэша `tokenizer.json` |
| `ApiDocs:Enabled` | `true` | публиковать OpenAPI/Scalar |
| `ApiDocs:Route` | `/scalar` | маршрут Scalar UI |
| `ApiDocs:OpenApiRoute` | `/openapi/{documentName}.json` | маршрут OpenAPI (обязателен `{documentName}`) |
| `ApiDocs:Title` | `TextChunker API` | заголовок документации |
| Kestrel `MaxRequestBodySize` | 20 МБ | ограничение тела запроса |

## Версионирование

Актуальная версия API — `/api/v1`. Все клиенты должны использовать маршруты с префиксом версии.

## Роуты

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/api/v1/health` | `{status, version}` |
| GET | `/api/v1/tokenizers` | `{names, defaultModel}` |
| GET | `/api/v1/strategies` | `{names}` — `fixed`, `recursive`, `paragraph`, `semantic` |
| GET | `/` | редирект на `/scalar` |
| GET | `/scalar` | Scalar UI (OpenAPI reference) |
| GET | `/openapi/v1.json` | OpenAPI 3.0 документ |
| POST | `/api/v1/chunk` | нарезка: JSON `{chunks, stats}` |
| POST | `/api/v1/chunk/stream` | NDJSON: по одному чанку на строку |
| POST | `/api/v1/chunk/file` | загрузка `.txt`/`.md`/`.markdown` (multipart) |

### Быстрые примеры

```bash
# GET /api/v1/health
curl http://localhost:5080/api/v1/health

# GET /api/v1/tokenizers
curl http://localhost:5080/api/v1/tokenizers

# GET /api/v1/strategies
curl http://localhost:5080/api/v1/strategies

# POST /api/v1/chunk
curl -X POST http://localhost:5080/api/v1/chunk \
  -H "Content-Type: application/json" \
  -d '{"text":"Hello world. Second sentence.","strategy":"recursive","tokenizer":"word","maxTokens":512}'

# POST /api/v1/chunk/stream
curl -N -X POST http://localhost:5080/api/v1/chunk/stream \
  -H "Content-Type: application/json" \
  -d '{"text":"abcdefghij","strategy":"fixed","maxChars":4,"tokenizer":"char"}'

# POST /api/v1/chunk/file
curl -X POST http://localhost:5080/api/v1/chunk/file \
  -F "file=@document.md" \
  -F "strategy=recursive" \
  -F "tokenizer=word" \
  -F "maxTokens=512"

# GET /scalar
curl http://localhost:5080/scalar

# GET /openapi/v1.json
curl http://localhost:5080/openapi/v1.json

# GET / (редирект на /scalar)
curl -I http://localhost:5080/
```

Для `strategy=semantic` добавьте заголовок `X-API-Key`, если на сервере задан
`TEXTCHUNKER_EMBEDDING_API_KEY`:

```bash
curl -X POST http://localhost:5080/api/v1/chunk \
  -H "Content-Type: application/json" \
  -H "X-API-Key: $TEXTCHUNKER_EMBEDDING_API_KEY" \
  -d '{"text":"…","strategy":"semantic","tokenizer":"huggingface","maxTokens":512}'
```

## Интерактивная документация (Scalar)

- UI: `/scalar` (корень `/` перенаправляет на `/scalar`).
- OpenAPI 3.0: `/openapi/v1.json` (Swashbuckle).
- UI: [Scalar](https://scalar.com/); ассеты отдаются локально, без CDN.
- В документе есть теги «Справочники»/«Нарезка», summary, описания схем из XML-комментариев,
  `problem+json` для 400/401/413/415/503 и NDJSON для `/api/v1/chunk/stream`.
- OpenAPI включает версионированные маршруты `/api/v1/...`.
- Отключение: `ApiDocs__Enabled=false` (роуты документации → 404, API работает).
- При обратном прокси с префиксом задайте `ApiDocs__Route` и `ApiDocs__OpenApiRoute`
  согласованно; `OpenApiRoute` должен содержать `{documentName}`.

```bash
# посмотреть документ
curl http://localhost:5080/openapi/v1.json

# UI
start http://localhost:5080/scalar
```

### `POST /api/v1/chunk`

```json
{
  "text": "Текст для нарезки…",
  "strategy": "recursive",
  "tokenizer": "huggingface",
  "tokenizerModel": "google/embeddinggemma-2",
  "tokenizerPath": null,
  "tokenizerCacheDir": null,
  "prefix": "title: none | text: ",
  "maxTokens": 512,
  "minTokens": 50,
  "overlapTokens": null,
  "trim": true,
  "dropSmall": false,
  "separators": ["\n\n", "\n"],
  "metadata": { "lang": "ru" },
  "source": "document.md"
}
```

- Все поля, кроме `text`, опциональны.
- Если ни один `max-*` не задан → `maxTokens = 512`; стратегия по умолчанию `recursive`,
  токенизатор `word`.
- `source` (по умолчанию `api`) задаёт top-level `source` и префикс `id` (`{source}:{seq:D6}`).
- `metadata` проходит в `metadata` каждого чанка.
- Для `huggingface` `tokenizerPath` (локальный `tokenizer.json`) приоритетнее
  `tokenizerModel`; `tokenizerCacheDir` переопределяет серверный.

Ответ:

```json
{
  "chunks": [
    {
      "id": "document.md:000001",
      "seq": 1,
      "source": "document.md",
      "text": "…",
      "startChar": 0,
      "endChar": 812,
      "charCount": 812,
      "byteCount": 1487,
      "tokenCount": 512,
      "metadata": { "strategy": "recursive", "tokenizer": "huggingface", "…": "…" }
    }
  ],
  "stats": {
    "chunkCount": 1,
    "minChunkTokens": 512,
    "avgChunkTokens": 512,
    "maxChunkTokens": 512,
    "inputChars": 812,
    "inputBytes": 1487,
    "elapsedMs": 12
  }
}
```

Для `strategy: "semantic"` в `stats` добавляются `semanticAtomCount`,
`embeddingCacheHits` и `embeddingCacheMisses`; `elapsedMs` включает построение
семантического плана (эмбеддинги).

### `POST /api/v1/chunk/stream`

Тот же запрос; ответ `200 application/x-ndjson`, каждая строка — JSON-чанк
(формат как у элементов `chunks[]`), разделитель `\n`. Выдача потоковая:
bounded-канал (backpressure), память ограничена окном нарезки; отключение клиента
останавливает обработку.

```bash
curl -N -X POST http://localhost:5080/api/v1/chunk/stream \
  -H "Content-Type: application/json" \
  -d '{"text":"abcdefghij","strategy":"fixed","maxChars":4,"tokenizer":"char"}'
```

### `POST /api/v1/chunk/file`

Загрузка файла `.txt`, `.md` или `.markdown` как `multipart/form-data`. Часть с файлом —
`file`; опции — обычные form-поля (те же, что у `/api/v1/chunk`, плюс `encoding`/`invalidEncoding`).

```bash
curl -X POST http://localhost:5080/api/v1/chunk/file \
  -F "file=@document.md" \
  -F "strategy=semantic" \
  -F "tokenizer=huggingface" \
  -F "tokenizerModel=google/embeddinggemma-2" \
  -F "maxTokens=512" \
  -F "embeddingProvider=llama-cpp" \
  -F "embeddingEndpoint=http://127.0.0.1:9932" \
  -F "embeddingModel=embeddinggemma-2" \
  -F "embeddingConcurrency=4" \
  -F "semanticPercentile=0.9" \
  -F "source=document.md" \
  -F "encoding=utf-8"
```

- Расширения: `.txt`, `.md`, `.markdown` (регистр не важен); иначе **415**.
- Кодировка по умолчанию UTF-8, BOM снимается; `encoding=windows-1251` и другие кодовые
  страницы; `invalidEncoding=replace|error` (по умолчанию `replace`; `error` → 400).
- `separators` и `metadata` передаются JSON-строками:
  `-F 'separators=["\n\n","\n"]'`, `-F 'metadata={"lang":"ru"}'`.
- Для `semantic` доступны плоские поля (`embeddingProvider`, `embeddingDimensions`,
  `embeddingMaxBatchChars`, `embeddingConcurrency`, `embeddingTimeoutSeconds`,
  `embeddingMaxRetries`, `semanticPercentile`,
  `semanticWindow`, `semanticMaxBytes`, `semanticStreamWindowBytes`,
  `semanticStreamOverlapBytes`, `semanticFallback`, …) или JSON-строки
  `embedding` / `semantic`. Плоские поля переопределяют JSON.
- Пустой файл → **400**; текст длиннее `Chunking:MaxTextChars` или тело > 20 МБ → **413**.
- Ответ — тот же `{chunks, stats}`, что и у `/api/v1/chunk`.
- Markdown не парсится — режется как обычный текст (сохраняются все разделители).

### Семантическая стратегия в API

```json
{
  "text": "…",
  "strategy": "semantic",
  "tokenizer": "huggingface",
  "maxTokens": 512,
  "embedding": {
    "provider": "llama-cpp",
    "endpoint": "http://127.0.0.1:9932",
    "model": "embeddinggemma-2",
    "batchSize": 32,
    "maxBatchChars": 8000,
    "concurrency": 4,
    "timeoutSeconds": 120,
    "maxRetries": 3,
    "useCache": true
  },
  "semantic": {
    "percentile": 0.9,
    "minDistance": 0.02,
    "window": 1,
    "maxBytes": 20000000,
    "streamWindowBytes": 4000000,
    "streamOverlapBytes": 256000,
    "fallback": "recursive"
  }
}
```

- `fallback: "streaming"` включает оконную обработку: документ читается окнами
  по `streamWindowBytes` с перекрытием `streamOverlapBytes`, `maxBytes` при этом
  не ограничивает размер файла.

- Ключ доступа к серверу эмбеддингов (`TEXTCHUNKER_EMBEDDING_API_KEY`) **не принимается**
  в теле — он задаётся только на сервере.
- Недоступный провайдер → **503**; документ больше `maxBytes` при `fallback=error` → **413**;
  неизвестный провайдер/невалидные параметры → **400**.
- Метаданные чанков содержат `semanticProvider`, `semanticModel`, `breakpointPercentile`
  и (при откате) `semanticFallback`. Подробно — [semantic-chunking.md](semantic-chunking.md).

### Аутентификация semantic-запросов

Если на сервере задана переменная окружения `TEXTCHUNKER_EMBEDDING_API_KEY`, то для запросов
`POST /api/v1/chunk`, `POST /api/v1/chunk/file` и `POST /api/v1/chunk/stream` со
`strategy: "semantic"` требуется заголовок:

```http
X-API-Key: <значение TEXTCHUNKER_EMBEDDING_API_KEY>
```

```bash
curl -X POST http://localhost:5080/api/v1/chunk \
  -H "Content-Type: application/json" \
  -H "X-API-Key: $TEXTCHUNKER_EMBEDDING_API_KEY" \
  -d '{"text":"…","strategy":"semantic","tokenizer":"huggingface","maxTokens":512}'
```

При отсутствии или неверном значении возвращается **401** `application/problem+json`.
Если переменная не задана, проверка отключается.

### Ошибки

Формат — `application/problem+json` (RFC 7807):

| Код | Причина |
|---|---|
| 400 | нет/пустой `text`, неизвестная стратегия/токенизатор, невалидные параметры, невалидный JSON |
| 401 | для `semantic` на `/api/v1/*` не передан или неверен `X-API-Key` (если задан `TEXTCHUNKER_EMBEDDING_API_KEY`) |
| 413 | `text` длиннее `Chunking:MaxTextChars` или тело больше лимита Kestrel |
| 415 | неподдерживаемое расширение файла (не `.txt`/`.md`/`.markdown`) или запрос не multipart |
| 503 | не удалось загрузить токенизатор (плохой `tokenizerPath`, сеть/парсинг) |
| 500 | непредвиденная ошибка |
| 499 | клиент отключился |

Пример:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Invalid request",
  "status": 400,
  "detail": "Неизвестная стратегия 'bogus'. Допустимые значения: fixed, recursive, paragraph. …"
}
```

## Токенизаторы и кэш

- Те же реализации, что в CLI; для `huggingface` — точный `tokenCount` (включая prefix).
- Успешно загруженный токенизатор кэшируется на время жизни процесса
  (`TokenizerCache`, singleton); повторные запросы не перезагружают модель.
- HF-модель скачивается при первом использовании в `tokenizerCacheDir` или
  `%LOCALAPPDATA%\TextChunker\tokenizers`.

## Ограничения MVP

- Аутентификация ограничена опциональным `X-API-Key` для semantic-запросов на `/api/v1/*`.
- Нет персистентности и фоновых задач.
- OpenAPI/Scalar подключён; документ доступен по `/openapi/v1.json`.
- Текст запроса не логируется.
