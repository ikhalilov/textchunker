#!/usr/bin/env sh
# Скачивает tokenizer.json целевой модели в текущий каталог.
# Файл (~32 МБ) нужен только для --tokenizer huggingface и не попадает в git.
set -eu

MODEL="${1:-google/embeddinggemma-2}"
OUTPUT="${2:-tokenizer.json}"
URL="https://huggingface.co/${MODEL}/resolve/main/tokenizer.json"

echo "Скачивание ${URL}"
curl -fL --retry 3 --output "${OUTPUT}" "${URL}"
echo "Сохранено: ${OUTPUT}"
echo "Использование: textchunker --tokenizer huggingface --tokenizer-path ${OUTPUT} ..."
