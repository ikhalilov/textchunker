# Токенизаторы

## Режимы

| `--tokenizer` | Единица | Точность | Описание |
|---|---|---|---|
| `huggingface` | токены модели | точная | `Microsoft.ML.Tokenizers` + `tokenizer.json` |
| `word` (по умолчанию) | слова | приближённая | максимальные отрезки не-пробельных символов |
| `char` | code points | приближённая | по Unicode code points (runes); эмодзи-цепочки — 1 токен |
| `regex` | совпадения | приближённая | `[\p{L}\p{N}]+` (слова без пунктуации) |
| `none` | — | — | `CountTokens = 0`, токенные лимиты не действуют |

Если `--tokenizer` не указан, используется `word`, а в stderr выводится предупреждение
о приблизительности.

## huggingface (точный режим)

```bash
textchunker --input doc.txt \
  --tokenizer huggingface \
  --tokenizer-model google/embeddinggemma-2 \
  --max-tokens 512
```

- Модель по умолчанию — `google/embeddinggemma-2` (byte-level BPE, словарь 262 144,
  `byte_fallback: true`).
- `tokenizer.json` (~32 МБ) скачивается один раз:
  `https://huggingface.co/<model>/resolve/main/tokenizer.json`.
- Кэш: `--tokenizer-cache-dir` или по умолчанию
  `%LOCALAPPDATA%/TextChunker/tokenizers/<sanitized-model>/tokenizer.json`
  (на Linux/macOS — `$XDG_DATA_HOME` / `~/.local/share`, через `LocalApplicationData`).
- После первой загрузки сеть не требуется — файл читается из кэша.
- Локальный файл можно указать напрямую:

```bash
textchunker --input doc.txt --tokenizer huggingface \
  --tokenizer-path ./tokenizer.json --max-tokens 512
```

Если загрузка/чтение/инициализация модели не удались, ошибка пишется в stderr,
код завершения — 5.

### Почему это точный подсчёт

`Microsoft.ML.Tokenizers` читает тот же `tokenizer.json` и реализует тот же BPE-движок,
что HF `transformers`/`tokenizers` (Rust). Подсчёт выполняется по выбранному токенизатору,
без эвристик вида `symbols / 4`.

Проверенные опорные значения (модель `google/embeddinggemma-2`):

| Текст | Токенов |
|---|---|
| `hello world` | 2 |
| `hello  world` | 2 (повторные пробелы схлопываются) |
| `a b c` | 3 |
| `日本語` | 9 (byte fallback: 3 символа × 3 байта) |
| `one\n\n two` | 2 |

Пробел сам по себе не является отдельным токеном.

### Лимит и task-префикс

Когда задан `--prefix`, лимит применяется к `prefix + chunk.text`:

```
tokenCount(prefix + chunk.text) <= --max-tokens
```

В метаданных чанка: `prefix`, `prefixTokens`. Финальный `tokenCount` каждого чанка
считается заново по итоговому тексту (склейка может менять число токенов на границе).

## Связь с эмбеддингами (semantic)

Токенизатор и провайдер эмбеддингов — **независимые** подсистемы:

- `--tokenizer huggingface` нужен только для точного подсчёта лимита чанка
  и читает `tokenizer.json` локально (или из кэша HF).
- `--strategy semantic` отправляет **текст** атомов на сервер эмбеддингов
  (`/v1/embeddings`); сервер сам токенизирует его своей моделью.
- Один и тот же `tokenizer.json` не требуется серверу, а серверу не нужен
  `--tokenizer-path` у CLI.
- Если `embeddinggemma-2` используется и там и там, удобно указать один
  локальный файл для CLI и запустить llama.cpp с тем же GGUF.

## Кэш и offline

```
%LOCALAPPDATA%\TextChunker\tokenizers\google_embeddinggemma-2\tokenizer.json
```

Удаление каталога приведёт к повторной загрузке при следующем запуске.
Для полностью offline-режима скопируйте `tokenizer.json` в кэш вручную или
используйте `--tokenizer-path`.

## Производительность

- Загрузка словаря 262k и ~515k merge-правил занимает порядка секунды и ~несколько
  сотен МБ памяти; выполняется один раз за процесс.
- Подсчёт токенов — O(длины текста); нарезка выполняет несколько пересчётов на чанк.
- Для очень больших файлов используйте `--max-tokens` и streaming-источник: чанки
  выводятся по мере чтения.
