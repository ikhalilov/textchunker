# Building, Testing, and Publishing

## Build

```bash
cd textchunker
dotnet build TextChunker.slnx
```

## Tests

Tests use xUnit v3 on the Microsoft Testing Platform; the mode is enabled by
`global.json` (`"test": { "runner": "Microsoft.Testing.Platform" }`).

```bash
# all tests: unit + integration + API
dotnet test TextChunker.slnx

# unit only
dotnet test tests/TextChunker.Tests

# API only
dotnet test tests/TextChunker.Api.Tests
```

Tests with the real HF tokenizer run only when `tokenizer.json`
is present in the checkout (otherwise they are silently skipped). To download the file:

```bash
pwsh scripts/fetch-tokenizer.ps1          # Windows
./scripts/fetch-tokenizer.sh              # Linux/macOS
```

Gated semantic integration tests against a live llama.cpp server:

```bash
TEXTCHUNKER_LLAMA_CPP_URL=http://127.0.0.1:9932 \
TEXTCHUNKER_LLAMA_CPP_MODEL=embeddinggemma-2 \
dotnet test tests/TextChunker.Tests --filter LlamaCppSemanticIntegration
```

## Load Tests

```bash
# 10 MB — always included in dotnet test
dotnet test TextChunker.slnx

# 100 MB and a single 10 MB line
TEXTCHUNKER_LOAD_TESTS=1   dotnet test tests/TextChunker.IntegrationTests

# + 1 GB
TEXTCHUNKER_LOAD_TESTS=1GB dotnet test tests/TextChunker.IntegrationTests
```

Results (Debug, local machine) and memory estimates are in
[troubleshooting.md](troubleshooting.md) (the "Memory and large files" section).

## Publishing

```bash
# framework-dependent (requires an installed .NET 11 Runtime)
dotnet publish src/TextChunker -c Release -o out

# cross-platform artifacts (framework-dependent)
dotnet publish src/TextChunker -c Release -r win-x64 --self-contained false -o out/win-x64
dotnet publish src/TextChunker -c Release -r linux-x64 --self-contained false -o out/linux-x64
dotnet publish src/TextChunker -c Release -r osx-x64 --self-contained false -o out/osx-x64

# self-contained (runtime bundled), if needed
dotnet publish src/TextChunker -c Release -r linux-x64 --self-contained true -o out/linux-x64-sc
```
