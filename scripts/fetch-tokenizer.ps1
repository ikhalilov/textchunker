# Скачивает tokenizer.json целевой модели в текущий каталог.
# Файл (~32 МБ) нужен только для --tokenizer huggingface и не попадает в git.
param(
    [string]$Model = "google/embeddinggemma-2",
    [string]$Output = "tokenizer.json"
)

$ErrorActionPreference = "Stop"
$url = "https://huggingface.co/$Model/resolve/main/tokenizer.json"

Write-Host "Скачивание $url"
Invoke-WebRequest -Uri $url -OutFile $Output
$size = (Get-Item $Output).Length
Write-Host "Сохранено: $Output ($size байт)"
Write-Host "Использование: textchunker --tokenizer huggingface --tokenizer-path $Output ..."
