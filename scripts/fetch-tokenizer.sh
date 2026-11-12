#!/usr/bin/env sh
# Downloads the target model's tokenizer.json into the current directory.
# The file (~32 MB) is only needed for --tokenizer huggingface and is not tracked in git.
set -eu

MODEL="${1:-google/embeddinggemma-2}"
OUTPUT="${2:-tokenizer.json}"
URL="https://huggingface.co/${MODEL}/resolve/main/tokenizer.json"

echo "Downloading ${URL}"
curl -fL --retry 3 --output "${OUTPUT}" "${URL}"
echo "Saved: ${OUTPUT}"
echo "Usage: textchunker --tokenizer huggingface --tokenizer-path ${OUTPUT} ..."
