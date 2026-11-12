# Semantic Boundary Calibration

A small synthetic corpus for checking boundary quality and picking
`--semantic-percentile` / `--semantic-window`. It is a **guideline**, not a
benchmark: documents are synthetic, boundaries are labeled by hand. There is
also a generated corpus of real Wikipedia articles (see below).

## Format

Next to each `NN-name.md` there is a `NN-name.json`:

```json
{
  "boundaries": [
    "# Следующий раздел",
    "Первое предложение новой темы."
  ]
}
```

The `boundaries` strings are substrings that start a new semantic segment
(usually a heading or the first sentence). The tool finds the position of the
substring, determines the atom containing it and counts a boundary as a hit
if the prediction is within ±1 atom of the truth.

## Running

A live llama.cpp server with `embeddinggemma-2` is required (see
[docs/semantic-chunking.md](../../docs/semantic-chunking.md)):

```bash
dotnet run --project tools/SemanticCalibration -- \
  --corpus tests/calibration/corpus \
  --endpoint http://127.0.0.1:9932 \
  --model embeddinggemma-2 \
  --percentiles 0.75,0.80,0.85,0.90,0.95 \
  --windows 1,2,3,5
```

Useful flags:

- `--verbose` — precision/recall/F1 and per-document boundary indices;
- `--dump-atoms` — atoms, `d_before` distances, true (`T`) and predicted
  (`B`) boundaries;
- `--cache <dir>` — embedding cache directory (default — a temporary directory);
- `--tolerance <n>` — boundary match tolerance in atoms (default 1);
- `--concurrency <n>` / `--batch-size <n>` — provider parameters;
- `--max-atom-tokens <n>` — atom size limit;
- `--min-distances 0.02,0.05` — sweep of `minDistance`.

## Current Corpus

10 documents, 23 labeled boundaries:

- RU/EN, Markdown headings, prose without headings, code fences, mixed
  languages;
- short subheadings ("Охрана труда", "Сборы") and long paragraphs;
- thematic shifts within paragraphs (stock market, a recipe, HR, law).

## Results (2026-11-04, `embeddinggemma-2-BF16`, 768d)

| Corpus | percentile | window | Precision | Recall | F1 |
|---|---:|---:|---:|---:|---:|
| all 10 | 0.90 | 1 | 0.629 | 0.957 | 0.759 |
| all 10 | 0.95 | 1 | 0.720 | 0.783 | 0.750 |
| prose only (5) | 0.90 | 1 | 0.818 | 0.900 | 0.857 |
| all 10 | 0.90 | 2 | 0.545 | 0.783 | 0.643 |
| all 10 | 0.90 | 3 | 0.515 | 0.739 | 0.607 |
| prose only (5) | 0.90 | 3 | 0.364 | 0.400 | 0.381 |

Takeaways:

- **percentile 0.90** remains the best default; 0.95 is more precise but
  loses recall.
- **window 1** beats window 3 on this corpus: averaging over three atoms
  smooths short thematic segments (a heading + 2–3 sentences).
- Markdown headings provide structural boundaries independently of embeddings;
  on prose, quality is determined only by the `1 − cos` distances
  (F1 0.857).

## Real Corpus (Wikipedia)

`fetch_wikipedia_corpus.py` downloads 40 articles (20 RU + 20 EN), extracts
section headings and uses the start of each section as a true boundary. The
texts (CC BY-SA 4.0) are not committed to git: the corpus is generated in
`wiki/` (gitignored).

```bash
python tests/calibration/fetch_wikipedia_corpus.py
dotnet run --project tools/SemanticCalibration -- \
  --corpus tests/calibration/wiki \
  --endpoint http://127.0.0.1:9932 --model embeddinggemma-2 \
  --percentiles 0.90,0.95,0.97 --windows 1,3,5 \
  --tolerance 2 --concurrency 4
```

Result: 39 documents, ~1.9 MB, ~340 boundaries (8.7 per document), 350
atoms per document.

| Corpus | percentile | window | Precision | Recall | F1 |
|---|---:|---:|---:|---:|---:|
| wiki (39 docs) | 0.90 | 1 | 0.126 | 0.515 | 0.203 |
| wiki (39 docs) | 0.95 | 3 | 0.207 | 0.426 | 0.279 |
| wiki (39 docs) | 0.95 | 5 | 0.222 | 0.456 | 0.298 |
| wiki (39 docs) | 0.97 | 3 | 0.265 | 0.335 | 0.296 |
| synthetic (10 docs) | 0.90 | 1 | 0.629 | 0.957 | 0.759 |

Takeaways:

- Wikipedia sections are editorial structure, not always a thematic shift:
  some sections are short and semantically close to their neighbors, so F1 is
  lower.
- For long encyclopedic documents `0.95–0.97` and window `3–5` are better;
  for short/structured texts — `0.90/1`.
- The `0.90/1` default is kept: it is much better on the synthetic corpus and
  has the higher average F1 across both corpora. For your own domain, run the
  tool and check `--verbose`.

## Adding a Document

1. Put `NN-name.md` into `corpus/`.
2. Create `NN-name.json` with substrings that start new topics.
3. Make sure the substring occurs exactly once in the text (otherwise
   `IndexOf` takes the first occurrence).
4. Run the tool and check `--dump-atoms` for debatable boundaries.
