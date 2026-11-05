# Архитектура

## Обзор

`TextChunker` — потоковый конвейер: источник текста → токенизатор → стратегия нарезки → writer.
Модули разделены интерфейсами, поэтому токенизатор, стратегию и формат вывода можно менять независимо.

```
ITextSource.Lines()
        │  (content, startChar, endChar) в join-пространстве "\n"
        ▼
ChunkingService  ── буфер + абсолютные смещения + metadata/seq/stats
        │
        ▼
IChunker.Chunk(text, tokenizer, options, final)
        │  ChunkSplit { Segments, ConsumedEnd, MoreNeeded }
        ▼
Chunk  ──►  IChunkWriter (JSON Lines / text)

Обёртки:
  CLI (src/TextChunker)        ── источники file/stdin, JSONL/text, exit-коды
  API (src/TextChunker.Api)    ── Minimal APIs, in-memory источник, JSON/NDJSON
```

## Слои

### Источники (`Sources/`)

- `ITextSource` — `Lines()`: `(Content, StartChar, EndChar)`.
- `FileTextSource` — файл, кодировка, BOM; ошибки → exit 3/4/2.
- `StdinTextSource` — `Console.In`; отсутствие перенаправления → exit 1.
- `TextLines` — `StreamReader`, снятие UTF-8 BOM, `--invalid-encoding`,
  NFC/NFKC/line-endings/whitespace-нормализация.
- Строки склеиваются `"\n"`; `StartChar`/`EndChar` — смещения в этом пространстве
  (`EndChar` исключающий). Именно на него опираются смещения чанков.

### Токенизаторы (`Tokenizers/`)

- `ITokenizer`: `CountTokens`, `CutAtTokenBoundary(text, n)` (0-based), `Name`, `ModelName`.
- `none`, `char` (code points, ZWJ/флаги/оттенки кожи), `word`, `regex` — приближённые.
- `huggingface` — точный BPE через `Microsoft.ML.Tokenizers` + `tokenizer.json`:
  кэш в `--tokenizer-cache-dir`, одна загрузка, далее offline; ошибки → `TokenizerLoadException` (exit 5).
- `TokenizerFactory` — имена и `ArgumentOutOfRangeException` для неизвестных (exit 2).
- `ChunkTokenCount.ForFinalChunk` — финальный пересчёт `prefix + text`.

### Нарезка (`Chunking/`)

- `ChunkingOptions` — единица (`Unit`), `MaxSize`, min/overlap, `Trim`, `DropSmall`,
  `Prefix`, `Separators`.
- `SizeMeasure` — размер в активной единице; для токенов учитывает prefix;
  `MaxCut` даёт максимальный префикс, укладывающийся в лимит (surrogate-safe).
- `FixedChunker` — фиксированные чанки + перекрытие.
- `SeparatorChunker` — общий рекурсивный движок: иерархия разделителей, жадная упаковка,
  hard-split fallback, перекрытие между выданными чанками.
- `RecursiveChunker` — `SeparatorChunker` с `options.Separators`.
- `ParagraphChunker` — сначала `\n\n`, затем остальные разделители.
- **Semantic** (`Chunking/Semantic/`): `SemanticAtomizer` (Markdown/предложения/код/сокращения),
  `SemanticPlanner` (эмбеддинги → `1 − cos` → перцентиль/окно), `SemanticPlan`, `SemanticChunker`.
  Эмбеддинги (`Embeddings/`): `IEmbeddingProvider`, `LlamaCppEmbeddingProvider` (HTTP
  `/v1/embeddings`, батчи/конкурентность, дедупликация текстов, ретраи 429/5xx,
  таймаут), `HashEmbeddingProvider` (офлайн), `EmbeddingCache` + `CachedEmbeddingProvider`.
- `ChunkerFactory` — `fixed|recursive|paragraph|semantic` (по умолчанию recursive).
- `ChunkingOptionsValidator` — `max > 0`, `min <= max`, `overlap < max` (exit 2).

### Сервис (`ChunkingService`)

- Читает строки потоково, поддерживает буфер и `bufferStart` (абсолютные смещения).
- Вызывает стратегию, пока та делает прогресс; `final=true` на EOF.
- Собирает `Chunk`: `id = "{source}:{seq:D6}"`, `seq`, `source`, `text`,
  `startChar`, `endChar`, `charCount`, `byteCount`, `tokenCount`, `metadata`.
- `metadata`: стратегия, токенизатор/модель, единица, `maxSize`, `normalized`,
  `prefix`/`prefixTokens`, пользовательские ключи. `--metadata source=` переопределяет `source`/`id`.
- Держит последние два чанка для `--min-*`/`--drop-small`; при отмене (Ctrl+C)
  дописывает уже собранные полные чанки.
- Считает статистику (`--stats`): `chunkCount`, `min/avg/maxChunkTokens`,
  `inputBytes`, `inputChars`, `elapsedMs`.

### Вывод (`Writers/`)

- `JsonLinesWriter` — один JSON-объект на строку, UTF-8 без BOM, camelCase-поля.
- `TextChunkWriter` — `=== CHUNK NNNNNN ===` + текст.
- Оба пишут целую строку за операцию — частичных JSON-записей не бывает.

### CLI (`Cli/`)

- `CliRunner` на `System.CommandLine`: разбор аргументов, значения по умолчанию,
  проверка `--invalid-encoding`/`--format`/`--normalize`/`--metadata`, запуск конвейера.
- Коды: 0 успех; 2 аргументы; 3 файл; 4 чтение/кодировка; 5 токенизатор; 6 запись; 1 прочее.
- При перенаправлении stdout/stderr — UTF-8 без BOM; интерактивная консоль переключается на UTF-8.
- `--version` печатает `AssemblyInformationalVersion`; `--help` — стандартный.

### API (`src/TextChunker.Api/`)

- ASP.NET Core Minimal APIs (net10.0), ссылается на `TextChunker`/`TextChunker.Abstractions`.
- Маршруты сгруппированы под `/api/v1` (`app.MapGroup`):
  `POST /api/v1/chunk` — JSON `{chunks, stats}`; `POST /api/v1/chunk/stream` — NDJSON
  (`application/x-ndjson`) через bounded `Channel<Chunk>` (backpressure, async-запись);
  `POST /api/v1/chunk/file` — multipart-загрузка `.txt`/`.md`/`.markdown` (кодировки, BOM);
  `GET /api/v1/health|tokenizers|strategies`.
- `SemanticApiKeyFilter` — единый endpoint filter для проверки `X-API-Key` у semantic-запросов,
  если задан `TEXTCHUNKER_EMBEDDING_API_KEY`.
- `ChunkingEngine` собирает `ChunkingOptions`/`ITokenizer`/`IChunker` из `ChunkRequest`;
  `InMemoryTextSource` даёт строки с абсолютными смещениями; `TokenizerCache` (singleton)
  переиспользует загруженные токенизаторы. Настройки читаются через `IOptionsMonitor`.
- Ошибки — RFC 7807: 400 (валидация), 401 (semantic без ключа), 413 (`MaxTextChars`/тело),
  415 (расширение файла), 503 (токенизатор), 500.
- Документация: Swashbuckle генерирует OpenAPI (`/openapi/v1.json`), Scalar UI — `/scalar`;
  XML-комментарии контрактов попадают в описания схем. Включается секцией `ApiDocs`.
- Ограничения: текст запроса не логируется; персистентности и фоновых задач нет.

## Контракт потоковой нарезки

`ITokenizer` и `IChunker` — stateless; состояние (буфер, смещения, seq) хранит сервис.

`ChunkSplit`:
- `Segments` — готовые чанки с **относительными** смещениями внутри переданного текста;
- `ConsumedEnd` — сколько символов буфера обработано (сервис делает `buffer.Remove(0, ConsumedEnd)`);
- `MoreNeeded` — фрагмент закончился внутри потенциального чанка, нужен ещё текст;
- `final` — вход закончился, остаток нужно выдать.

Инварианты: лимит соблюдён; нет пустых чанков; смещения не убывают; при непустом
буфере стратегия либо выдаёт чанк, либо возвращает `MoreNeeded`/`ConsumedEnd` с прогрессом.

## Расширение

- **Токенизатор:** реализовать `ITokenizer`, добавить имя в `TokenizerFactory.SupportedNames`.
- **Стратегия:** реализовать `IChunker`, добавить имя в `ChunkerFactory`.
- **Формат вывода:** реализовать `IChunkWriter`, выбрать его в `CliRunner.CreateWriter`.
- **Источник:** реализовать `ITextSource` (строки + смещения в join-пространстве).

## Структура проекта

```
textchunker/
├── src/TextChunker.Abstractions/   интерфейсы, Chunk, ChunkingOptions, ChunkTokenCount
├── src/TextChunker/
│   ├── Sources/                    файл/stdin, кодировки, BOM, нормализация
│   ├── Tokenizers/                 none/char/word/regex/huggingface + фабрика
│   ├── Chunking/                   стратегии, SizeMeasure, сервис, валидация
│   ├── Writers/                    JSONL/text
│   ├── Cli/                        CliRunner + Program.cs
│   └── bin/, obj/                  артефакты сборки (gitignored)
├── src/TextChunker.Api/            Minimal APIs: contracts, services, Program.cs
├── tests/TextChunker.Tests/        unit + property + cancellation
├── tests/TextChunker.IntegrationTests/  CLI-процессы, golden, нагрузочные
├── tests/TextChunker.Api.Tests/    HTTP-тесты (Kestrel + HttpClient)
├── docs/                           usage, chunking-strategies, tokenizers, api, troubleshooting, architecture
├── README.md
└── TextChunker.slnx
```

## Тесты и CI

- Unit: токенизаторы, чанкеры, сервис, property-инварианты, отмена.
- Integration: реальный процесс `dotnet TextChunker.dll` — коды выхода, JSONL, stdin,
  `--stats`, golden, 10 МБ (100 МБ/1 ГБ — по `TEXTCHUNKER_LOAD_TESTS`).
- API: запуск реального `TextChunker.Api.dll` на свободном порту и HTTP-проверки
  (health, chunk, stream, 400/401/413/415/503, source/metadata, `X-API-Key`, кэш токенизаторов).
- CI (`.github/workflows/ci.yml`): матрица Ubuntu/Windows/macOS, .NET 10 (`net10.0`, SDK для `.slnx`).

## Дальнейшие улучшения

Рекомендации из обзоров `dotnet-best-practices`, `dotnet-design-pattern-review`,
`csharp-concurrency-patterns` и `csharp-nullable-reference-types`:

- перевести `ChunkingEngine` и другие сервисы на primary-constructor синтаксис — выполнено;
- добавить data-annotation валидацию в `ApiOptions` и включить `ValidateDataAnnotations()` —
  выполнено;
- добавить `ConfigureAwait(false)` в библиотечных async-вызовах — проверено, уже используется;
- дополнить XML-документацию оставшихся публичных типов и методов — выполнено для ключевых
  типов API;
- оценить лёгкую абстракцию command-handler для будущих команд CLI — future work;
- оценить repository-абстракцию при появлении персистентности — future work;
- оценить `ResourceManager` для локализации логов и сообщений об ошибках — future work.

Актуальный чек-лист ведётся в `openspec/changes/api-design-recommendations/tasks.md`.
