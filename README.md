# TextChunker

Кроссплатформенная консольная утилита на C# / .NET 10 для потоковой нарезки текста
на чанки под RAG/векторизацию. Читает файл или `stdin`, режет по стратегии
(`fixed` / `recursive` / `paragraph` / `semantic`), считает размер в токенах/символах/словах/байтах
и пишет JSON Lines (или plain text).

## Возможности

- Потоковое чтение без загрузки всего файла в память.
- Семантическая нарезка (`--strategy semantic`) через локальный llama.cpp/`embeddinggemma-2`
  или офлайн-провайдер `hash`; для больших файлов — оконный `--semantic-fallback streaming`.
- Точный подсчёт токенов целевой модели: `huggingface` (`Microsoft.ML.Tokenizers` +
  `tokenizer.json` от `google/embeddinggemma-2`, byte-level BPE). Файл скачивается один раз
  в кэш, дальше работа полностью offline.
- Приближённые режимы: `word` (по умолчанию, с предупреждением в stderr), `char`, `regex`, `none`.
- Task-префикс (`--prefix`), учитываемый в лимите токенов; финальный пересчёт `tokenCount`
  по итоговому тексту чанка (`prefix + chunk`).
- Перекрытие (`--overlap-tokens` / `--overlap-chars`), минимум (`--min-*`), `--drop-small`, `--trim`.
- Настраиваемые разделители `recursive` (`--separators`).
- Кодировки (`--encoding`, по умолчанию UTF-8), BOM, `--invalid-encoding replace|error`,
  опциональная нормализация (`--normalize`).
- JSON Lines с полями `id`, `seq`, `source`, `text`, `startChar`, `endChar`, `charCount`,
  `byteCount`, `tokenCount`, `metadata`.
- `--stats` — агрегированная статистика в stderr (stdout остаётся чистым JSONL);
  для `semantic` — ещё `semanticAtomCount` и статистика кэша эмбеддингов.
- Стабильные коды завершения; ошибки и предупреждения — только в stderr.
- HTTP API (ASP.NET Core Minimal APIs) — `/api/v1/chunk`, `/api/v1/chunk/stream` (NDJSON), `/api/v1/health`.

## Сборка и тесты

```bash
cd textchunker
dotnet build TextChunker.slnx
dotnet test TextChunker.slnx
```

Тесты — xUnit v3 на Microsoft Testing Platform; режим включается `global.json`
(`"test": { "runner": "Microsoft.Testing.Platform" }`).

Тесты с реальным HF-токенизатором включаются, только если `tokenizer.json`
лежит в checkout (иначе они молча пропускаются). Скачать файл:

```bash
pwsh scripts/fetch-tokenizer.ps1          # Windows
./scripts/fetch-tokenizer.sh              # Linux/macOS
```

Gated-тесты семантики против живого llama.cpp:

```bash
TEXTCHUNKER_LLAMA_CPP_URL=http://127.0.0.1:9932 \
TEXTCHUNKER_LLAMA_CPP_MODEL=embeddinggemma-2 \
dotnet test tests/TextChunker.Tests --filter LlamaCppSemanticIntegration
```

Публикация:

```bash
# framework-dependent (нужен установленный .NET 10 Runtime)
dotnet publish src/TextChunker -c Release -o out

# кроссплатформенные артефакты (framework-dependent)
dotnet publish src/TextChunker -c Release -r win-x64 --self-contained false -o out/win-x64
dotnet publish src/TextChunker -c Release -r linux-x64 --self-contained false -o out/linux-x64
dotnet publish src/TextChunker -c Release -r osx-x64 --self-contained false -o out/osx-x64

# self-contained (runtime включён), при необходимости
dotnet publish src/TextChunker -c Release -r linux-x64 --self-contained true -o out/linux-x64-sc
```

## Быстрый старт

```bash
# stdin → stdout (word-токенизатор по умолчанию, приближённо)
cat document.txt | dotnet run --project src/TextChunker -- --input -

# файл → JSONL-файл, 512 токенов модели, без приближения
dotnet run --project src/TextChunker -- \
  --input document.txt --output chunks.jsonl \
  --tokenizer huggingface --tokenizer-model google/embeddinggemma-2 \
  --strategy recursive --max-tokens 512 --stats

# task-префикс учитывается в лимите
dotnet run --project src/TextChunker -- \
  --input document.txt --tokenizer huggingface \
  --prefix "title: none | text: " --max-tokens 512

# plain-text вывод с маркерами
dotnet run --project src/TextChunker -- --input document.txt --format text --max-chars 800

# семантика: локальный llama.cpp (сервер уже запущен, см. docs/semantic-chunking.md)
dotnet run --project src/TextChunker -- \
  --input document.md --strategy semantic \
  --embedding-provider llama-cpp --embedding-endpoint http://127.0.0.1:9932 \
  --embedding-model embeddinggemma-2 \
  --tokenizer huggingface --tokenizer-path ./tokenizer.json --max-tokens 512
```

## Web API

```bash
dotnet run --project src/TextChunker.Api --urls http://localhost:5080
```

| Метод | Путь | Назначение |
|---|---|---|
| GET | `/api/v1/health` | статус и версия |
| GET | `/api/v1/tokenizers` | список токенизаторов и модель по умолчанию |
| GET | `/api/v1/strategies` | список стратегий |
| GET | `/scalar` | интерактивная документация Scalar |
| GET | `/openapi/v1.json` | OpenAPI-документ |
| POST | `/api/v1/chunk` | нарезка → `{chunks, stats}` |
| POST | `/api/v1/chunk/stream` | потоковый NDJSON |
| POST | `/api/v1/chunk/file` | загрузка `.txt`/`.md`/`.markdown` |

Для semantic-запросов на `/api/v1/*` заголовок `X-API-Key` обязателен, если на сервере задана
переменная `TEXTCHUNKER_EMBEDDING_API_KEY`.

Документация включена по умолчанию; корень `/` перенаправляет на `/scalar`.
Настройки — секция `ApiDocs` (`Enabled`, `Route`, `OpenApiRoute`, `Title`).

Подробно: [docs/api.md](docs/api.md).

## Основные параметры

| Параметр | Описание |
|---|---|
| `--input <path>` | Входной файл; `-` или отсутствие — stdin |
| `--output <path>` | Файл вывода (по умолчанию stdout) |
| `--strategy` | `fixed`, `recursive` (по умолчанию), `paragraph`, `semantic` |
| `--tokenizer` | `none`, `char`, `word` (по умолчанию), `regex`, `huggingface` |
| `--tokenizer-model` | HF-модель (по умолчанию `google/embeddinggemma-2`) или путь к `tokenizer.json` |
| `--tokenizer-path` | Явный путь к локальному `tokenizer.json` |
| `--tokenizer-cache-dir` | Каталог кэша модели |
| `--prefix` | Task-префикс, учитываемый в лимите токенов |
| `--max-tokens/-chars/-words/-bytes` | Максимальный размер чанка |
| `--min-tokens/-chars/-words` | Минимум для финального чанка |
| `--overlap-tokens` / `--overlap-chars` | Перекрытие соседних чанков |
| `--trim` / `--no-trim` | Обрезка пробелов по краям (по умолчанию включена) |
| `--drop-small` | Отбросить финальный чанк меньше `--min-*` |
| `--separators` | Разделители `recursive` по убыванию приоритета |
| `--format` | `jsonl` (по умолчанию) или `text` |
| `--encoding` | Кодировка входа (по умолчанию `utf-8`) |
| `--invalid-encoding` | `replace` (по умолчанию) или `error` |
| `--normalize` | `nfc`, `nfkc`, `line-endings`, `whitespace`, `all` (через запятую) |
| `--metadata key=value` | Пользовательские метаданные (можно несколько; `source=` переопределяет `source`/`id`) |
| `--stats` | Статистика JSON в stderr |
| `--version`, `--help` | Версия и справка |

Семантическая стратегия:

| Параметр | Описание |
|---|---|
| `--embedding-provider` | `llama-cpp` (по умолчанию) или `hash` (офлайн) |
| `--embedding-endpoint` | URL сервера (по умолчанию `http://127.0.0.1:9932`) |
| `--embedding-model` | Имя модели в запросе к серверу |
| `--embedding-batch-size` | Атомов на HTTP-запрос (по умолчанию 32) |
| `--embedding-max-batch-chars` | Символьный бюджет батча (8000; `0` — только по количеству) |
| `--embedding-dimensions` | Ожидаемая размерность векторов (768; `0` — не проверять) |
| `--embedding-concurrency` | Параллельных батчей (по умолчанию 1; нужно `--parallel N` на сервере) |
| `--embedding-timeout` | Таймаут запроса, секунды (по умолчанию 120) |
| `--embedding-max-retries` | Повторы на сеть/429/5xx (по умолчанию 3) |
| `--embedding-prompt` | Префикс к каждому атому перед эмбеддингом |
| `--embedding-cache-dir` / `--no-embedding-cache` | Каталог кэша / отключить кэш |
| `--embedding-cache-max-bytes` | Лимит размера кэша (автоочистка) |
| `--embedding-cache-ttl-days` | TTL записей кэша в днях (автоочистка) |
| `--prune-cache` / `--prune-cache-dry-run` | Обслужить кэш и выйти (dry-run — без удаления) |
| `--semantic-percentile` | Перцентиль расстояний для границ (0.90) |
| `--semantic-min-distance` | Минимальная дистанция границы (0.02) |
| `--semantic-window` | Окно TextTiling (по умолчанию 1 — соседние атомы) |
| `--semantic-fallback` | `recursive` (по умолчанию), `error` или `streaming` |
| `--semantic-stream-window-bytes` | Размер окна streaming, байт (4000000) |
| `--semantic-stream-overlap-bytes` | Перекрытие окон streaming, байт (256000) |
| `--semantic-max-bytes` | Лимит входа document-mode (20 МБ) |
| `--semantic-min-atom-chars` / `--semantic-max-atom-tokens` | Склейка коротких / дробление длинных атомов |

Если ни один `--max-*` не задан, используется `--max-tokens 512`.

## Коды завершения

| Код | Значение |
|---|---|
| 0 | Успех |
| 1 | Общая ошибка / stdin не переопределён |
| 2 | Невалидные аргументы (стратегия, токенизатор, overlap, min/max и т.п.) |
| 3 | Файл не найден |
| 4 | Ошибка чтения / невалидная кодировка |
| 5 | Ошибка загрузки токенизатора модели или провайдера эмбеддингов |
| 6 | Ошибка записи |

## Кириллица в консоли/отладчике

При наличии интерактивной консоли утилита переключает её на UTF-8, а при перенаправлении
stdout/stderr использует UTF-8-writer'ы без BOM. Если в окне отладчика кириллица всё равно
отображается как `?`, включите UTF-8 в настройках терминала/окна вывода и шрифт с поддержкой
кириллицы (Consolas, Cascadia Mono и т.п.).

## Пример JSONL-записи

```json
{"id":"document.txt:000001","seq":1,"source":"document.txt","text":"Первый чанк...","startChar":0,"endChar":812,"charCount":812,"byteCount":1487,"tokenCount":512,"metadata":{"strategy":"recursive","tokenizer":"huggingface","tokenizerModel":"google/embeddinggemma-2","unit":"tokens","normalized":false,"maxSize":512}}
```

Документация: [usage.md](docs/usage.md), [chunking-strategies.md](docs/chunking-strategies.md),
[tokenizers.md](docs/tokenizers.md), [semantic-chunking.md](docs/semantic-chunking.md),
[api.md](docs/api.md), [troubleshooting.md](docs/troubleshooting.md),
[architecture.md](docs/architecture.md).

## Нагрузочные тесты

```bash
# 10 МБ — всегда входит в dotnet test
dotnet test TextChunker.slnx

# 100 МБ, 1 ГБ и одна строка 10 МБ — по требованию
TEXTCHUNKER_LOAD_TESTS=1   dotnet test tests/TextChunker.IntegrationTests   # 100 МБ + 10 МБ строка
TEXTCHUNKER_LOAD_TESTS=1GB dotnet test tests/TextChunker.IntegrationTests   # + 1 ГБ
```

### Результаты (Debug, локальная машина)

| Вход | Время | Пиковый RSS | Чанков |
|---|---:|---:|---:|
| 10 МБ, строки | 0.7 с | 46.6 МиБ | 7 282 |
| 100 МБ, строки | 3.6 с | 49.8 МиБ | 72 818 |
| 1 ГБ, строки | 30.0 с | 48.6 МиБ | 745 655 |
| 10 МБ, одна строка | 0.5 с | 140.9 МиБ | 7 282 |

Параметры: `--strategy fixed --max-chars 1000 --tokenizer char`; вход — повторяющаяся
RU/EN-строка. RSS семплировался каждые 10 мс (`psutil`). На больших файлах память
ограничена окном нарезки, а не размером файла (для одной строки — размером строки).
