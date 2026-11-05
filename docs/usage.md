# Использование

## Вход и выход

```bash
# файл → stdout
textchunker --input document.txt

# stdin → stdout
cat document.txt | textchunker --input -
cat document.txt | textchunker            # stdin по умолчанию

# файл → файл (UTF-8 без BOM)
textchunker --input document.txt --output chunks.jsonl

# plain text с маркерами === CHUNK 000001 ===
textchunker --input document.txt --format text
```

Если `--input` не задан и stdin не перенаправлен, утилита пишет ошибку в stderr
и завершается с кодом 1 (чтобы не блокироваться на терминале).

## Версия и справка

```bash
textchunker --help
textchunker --version      # 0.2.0
```

## Кодировки и BOM

- По умолчанию вход декодируется как UTF-8; UTF-8 BOM отбрасывается и не попадает в текст.
- `--encoding windows-1251` (и другие кодовые страницы, включая `koi8-r`, `iso-8859-1`)
  подключаются через `CodePagesEncodingProvider`.
- Невалидные последовательности по умолчанию заменяются на `U+FFFD`;
  `--invalid-encoding error` останавливает обработку с кодом 4.

## Нормализация

По умолчанию текст не меняется (смещения соответствуют исходному тексту после снятия BOM).
`--normalize` включает нормализацию; смещения `startChar`/`endChar` в этом случае указывают
в нормализованный текст, а в метаданных `normalized = true`.

```bash
textchunker --input doc.txt --normalize nfkc
textchunker --input doc.txt --normalize "line-endings,whitespace"
textchunker --input doc.txt --normalize all
```

## Лимиты и единицы измерения

Активная единица определяется тем, какой `--max-*` задан
(приоритет: tokens > chars > words > bytes). Если не задан ни один — используется
`--max-tokens 512`.

```bash
textchunker --input doc.txt --max-chars 800
textchunker --input doc.txt --max-words 120
textchunker --input doc.txt --max-bytes 2048
textchunker --input doc.txt --tokenizer huggingface --max-tokens 512
```

Минимум и финальный чанк:

```bash
# финальный чанк меньше 50 токенов будет отброшен
textchunker --input doc.txt --max-tokens 512 --min-tokens 50 --drop-small

# без --drop-small финальный чанк сохраняется (по возможности объединяется с предыдущим)
textchunker --input doc.txt --max-tokens 512 --min-tokens 50
```

Перекрытие:

```bash
textchunker --input doc.txt --strategy fixed --max-tokens 100 --overlap-tokens 20
textchunker --input doc.txt --strategy fixed --max-chars 500 --overlap-chars 100
```

## Task-префикс

`--prefix` учитывается в лимите: гарантируется
`tokenCount(prefix + chunk.text) <= max-tokens`. В метаданных появляются
`prefix` и `prefixTokens`.

```bash
textchunker --input doc.txt --tokenizer huggingface \
  --prefix "title: none | text: " --max-tokens 512
```

## Метаданные

```bash
textchunker --input doc.txt --metadata lang=ru --metadata project=kb
```

Пользовательские ключи попадают в объект `metadata` каждого чанка. Служебные ключи
(`strategy`, `tokenizer`, `tokenizerModel`, `unit`, `normalized`, `maxSize`, `prefix`,
`prefixTokens`) добавляются автоматически.

`--metadata source=<name>` дополнительно переопределяет верхнеуровневое поле `source`
и префикс детерминированного `id` (по умолчанию источник — имя файла или `stdin`).

## Семантическая нарезка

```bash
# локальный llama.cpp
textchunker --input doc.md --strategy semantic \
  --embedding-provider llama-cpp \
  --embedding-endpoint http://127.0.0.1:9932 \
  --embedding-model embeddinggemma-2 \
  --embedding-batch-size 32 --embedding-concurrency 4 \
  --tokenizer huggingface --tokenizer-path ./tokenizer.json \
  --max-tokens 512 --prefix "title: none | text: " --stats

# офлайн (детерминированный провайдер, без модели)
textchunker --input doc.md --strategy semantic \
  --embedding-provider hash --no-embedding-cache --max-tokens 512
```

- Текст буферизуется до `--semantic-max-bytes` (20 МБ); больший документ
  откатывается на `recursive` или завершается ошибкой (`--semantic-fallback`).
- Границы настраиваются `--semantic-percentile`, `--semantic-min-distance`,
  `--semantic-window`.
- Все обычные гарантии сохраняются: лимиты с `--prefix`, `--min-*`, `--drop-small`,
  `--trim`, `--overlap-*`, детерминированные id и смещения.
- Подробности — [semantic-chunking.md](semantic-chunking.md).

## Статистика

`--stats` пишет JSON в **stderr** (stdout остаётся чистым JSONL):

```json
{"chunkCount":14,"minChunkTokens":16,"avgChunkTokens":31.5,"maxChunkTokens":40,"inputBytes":415,"inputChars":245,"elapsedMs":77}
```

Для семантической стратегии добавляются диагностические поля:

```json
{"chunkCount":8,"semanticAtomCount":23,"embeddingCacheHits":18,"embeddingCacheMisses":5}
```

`embeddingCacheHits`/`embeddingCacheMisses` появляются, только если кэш включён;
`semanticAtomCount` — только для `--strategy semantic`. Для semantic `elapsedMs`
включает построение плана (эмбеддинги), то есть соответствует реальному времени прогона.

## Формат JSONL

Одна JSON-запись на чанк, по строке на объект, UTF-8 без BOM:

```json
{
  "id": "document.txt:000001",
  "seq": 1,
  "source": "document.txt",
  "text": "...",
  "startChar": 0,
  "endChar": 812,
  "charCount": 812,
  "byteCount": 1487,
  "tokenCount": 512,
  "metadata": { "...": "..." }
}
```

- `id` детерминирован: `{source}:{seq:D6}`.
- `seq` начинается с 1.
- `startChar` / `endChar` — смещения в исходном (или нормализованном) тексте;
  `endChar` исключающий.
- `charCount` — длина текста в UTF-16 code units, `byteCount` — размер UTF-8.
- `tokenCount` — точный пересчёт по итоговому тексту `prefix + text`.
- Смещения и `charCount` соответствуют `text` после `--trim` (пробелы по краям
  не входят в чанк).

## Коды завершения

| Код | Значение |
|---|---|
| 0 | Успех |
| 1 | Общая ошибка / stdin не переопределён |
| 2 | Невалидные аргументы |
| 3 | Файл не найден |
| 4 | Ошибка чтения / невалидная кодировка |
| 5 | Ошибка загрузки токенизатора |
| 6 | Ошибка записи |

## Ctrl+C

При прерывании обработка останавливается на границе строки; уже завершённые чанки
дописываются, недописанная JSON-строка не появляется. Код завершения — 0.

## См. также

- [chunking-strategies.md](chunking-strategies.md) — стратегии, единицы, перекрытие.
- [semantic-chunking.md](semantic-chunking.md) — семантическая нарезка, streaming, llama.cpp, тюнинг.
- [tokenizers.md](tokenizers.md) — режимы токенизации и кэш модели.
- [troubleshooting.md](troubleshooting.md) — кодировки, stdin, exit-коды, память.
- [architecture.md](architecture.md) — устройство и точки расширения.
