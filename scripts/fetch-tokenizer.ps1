# Downloads the target model's tokenizer.json into the current directory.
# The file (~32 MB) is only needed for --tokenizer huggingface and is not tracked in git.
param(
    [string]$Model = "google/embeddinggemma-2",
    [string]$Output = "tokenizer.json"
)

$ErrorActionPreference = "Stop"
$url = "https://huggingface.co/$Model/resolve/main/tokenizer.json"

Write-Host "Downloading $url"
Invoke-WebRequest -Uri $url -OutFile $Output
$size = (Get-Item $Output).Length
Write-Host "Saved: $Output ($size bytes)"
Write-Host "Usage: textchunker --tokenizer huggingface --tokenizer-path $Output ..."
