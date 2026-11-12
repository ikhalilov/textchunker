# Chunking Strategies

## Units

The active unit is determined by the specified `--max-*`:

| Parameter | Unit | How it is measured |
|---|---|---|
| `--max-tokens` | tokens | by the selected tokenizer (`prefix + text`) |
| `--max-chars` | characters | UTF-16 code units |
| `--max-words` | words | runs of non-whitespace characters |
| `--max-bytes` | bytes | UTF-8 |

Priority when several `--max-*` are set: tokens > chars > words > bytes.
Guarantee: size of every chunk in the active unit is `<= max`.

By default (when no `--max-*` is set) — `--max-tokens 512`.

## fixed

Each chunk is a maximal prefix of the remainder that fits within the limit.

```bash
textchunker --input doc.txt --strategy fixed --max-chars 500
textchunker --input doc.txt --strategy fixed --max-tokens 100 --overlap-tokens 20
```

- Overlap: the next chunk starts `overlap` units before the end of the previous
  one. For tokens the boundary is computed by the tokenizer; for characters —
  arithmetically.
- Progress is guaranteed: overlap is always smaller than the limit (validation
  `overlap < max`), and a chunk cannot be smaller than one unit.
- Surrogate pairs are never split.

## recursive (default)

Hierarchical splitting by a separator list, from coarse to fine:

```
"\n\n" → "\n" → ". " → "! " → "? " → "; " → ", " → " " → "" (hard cut)
```

Algorithm:

1. The text is recursively split by separators into parts, each of which fits
   within the limit.
2. Parts are greedily packed into chunks: a part is added until the size would
   exceed the limit.
3. If a part does not fit even on its own, it is split by the next separator,
   and at the end the list ends with a hard cut by the limit.

```bash
textchunker --input doc.txt --strategy recursive --max-tokens 512
```

### Custom separators

`--separators` takes several values in priority order. Escape sequences `\n`,
`\r`, `\t` are supported; the `$` prefix allows passing a literal without
processing.

```bash
textchunker --input doc.txt --strategy recursive --separators "\n\n" "\n" ". " " "
textchunker --input doc.txt --strategy recursive --separators "$|" "$||"
```

An empty string (`""`) means a hard cut.

**Known limitation:** Russian abbreviations (`т. д.`, `т. п.`, `см.`, `стр.`)
are not recognized as a separate case. On the hard-cut fallback the boundary
may pass inside an abbreviation; the text is not lost and the limit is observed,
but semantically even sentence boundaries are not guaranteed.

## paragraph

First the text is split on blank lines (`\n\n`), then:

- small paragraphs are merged into chunks up to the limit;
- a paragraph exceeding the limit is sub-split by the remaining separators
  (recursive/hard-cut fallback).

```bash
textchunker --input doc.txt --strategy paragraph --max-tokens 512
```

## semantic

Boundaries are set by meaning: atoms (sentences/paragraphs/headings/code/lists)
are compared with embeddings, and a chunk ends where the topic changes.

```bash
textchunker --input doc.md --strategy semantic \
  --embedding-provider llama-cpp --embedding-endpoint http://127.0.0.1:9932 \
  --embedding-model embeddinggemma-2 --max-tokens 512
```

- By default it works in document-mode: text is buffered up to
  `--semantic-max-bytes` (20 MB), then a boundary plan is built and chunking
  is executed.
- For large files: `--semantic-fallback streaming` — windows of
  `--semantic-stream-window-bytes` (4 MB) with overlap; memory is bounded by
  the window.
- Boundaries: percentile of `1 − cos` distances (`--semantic-percentile`),
  minimum distance (`--semantic-min-distance`), optional TextTiling window
  (`--semantic-window`).
- Limits, `--prefix`, `--min-*`, `--drop-small`, `--trim`, and `--overlap-*`
  have the same guarantees as with the other strategies; overlap is accumulated
  from whole atoms.
- Without streaming, large documents fall back to `recursive`
  (`--semantic-fallback recursive`) or finish with an error
  (`--semantic-fallback error`).
- Without a server: `--embedding-provider hash` (deterministic, offline).

Details and tuning — [semantic-chunking.md](semantic-chunking.md).

## Minimum and final chunk

- `--min-tokens` / `--min-chars` / `--min-words` set the minimum for the
  **final** chunk.
- `--drop-small` — discard the final chunk if it is smaller than the minimum.
- Without `--drop-small` the final chunk is kept; if it is smaller than the
  minimum and merging it with the previous chunk does not exceed the limit,
  the chunks are merged (`\n\n` for `paragraph`, otherwise a space).

```bash
textchunker --input doc.txt --max-tokens 512 --min-tokens 50 --drop-small
```

## Trim and offsets

`--trim` is on by default: whitespace/newlines at the edges of a chunk do not
enter `text`, and `startChar`/`endChar` point at the trimmed boundaries.
`--no-trim` preserves edge whitespace (convenient for exact restoration of the
input).

## Streaming processing

The source is read line by line; the strategy receives a buffer and returns:

- `Segments` — ready chunks (text + offsets within the buffer);
- `ConsumedEnd` — how many characters of the buffer have been processed;
- `MoreNeeded` — the fragment ended inside a potential chunk, more text is
  needed.

This is why memory is bounded by the buffer size (approximately one chunk + one
line), and chunks are emitted as they are read, not after loading the whole file.

## Invariants (checked by tests)

- size of every chunk in the active unit is `<= max`;
- there are no empty chunks;
- `startChar`/`endChar` never decrease and stay within the input text;
- for `fixed` + `--no-trim` without overlap, concatenating chunks restores the
  input;
- repeated runs with the same input and parameters produce identical
  `id`/`seq`/text.
