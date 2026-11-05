using System.Text;

namespace TextChunker.Api.Services;

/// <summary>
/// Чтение загруженного `.txt`/`.md`/`.markdown` файла: проверка расширения и размера,
/// кодировка (UTF-8 по умолчанию, кодовые страницы), BOM, режим invalid-encoding.
/// </summary>
public static class FileUploadReader
{
    private static readonly string[] AllowedExtensions = { ".txt", ".md", ".markdown" };

    public static async Task<string> ReadAsync(
        IFormFile? file,
        string? encodingName,
        string? invalidEncoding,
        int maxTextChars,
        CancellationToken cancellationToken)
    {
        if (file is null || file.Length == 0)
        {
            throw new ArgumentException("Файл не передан или пуст.");
        }

        string extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnsupportedMediaTypeException(
                $"Поддерживаются только .txt, .md, .markdown (получено '{extension}').");
        }

        long maxBytes = (long)maxTextChars * 4 + 4;
        if (file.Length > maxBytes)
        {
            throw new PayloadTooLargeException($"Файл больше лимита ({maxBytes} байт).");
        }

        bool invalidAsError = ParseInvalidEncoding(invalidEncoding);
        Encoding encoding = ResolveEncoding(encodingName, invalidAsError);

        using Stream stream = file.OpenReadStream();
        using var reader = new StreamReader(
            stream,
            encoding,
            detectEncodingFromByteOrderMarks: true,
            bufferSize: 4096,
            leaveOpen: false);

        string text = await reader.ReadToEndAsync(cancellationToken);

        if (text.Length == 0)
        {
            throw new ArgumentException("Файл пуст.");
        }
        if (text.Length > maxTextChars)
        {
            throw new PayloadTooLargeException($"Текст длиннее лимита {maxTextChars} символов.");
        }

        return text;
    }

    private static bool ParseInvalidEncoding(string? value)
    {
        return (value ?? "replace").Trim().ToLowerInvariant() switch
        {
            "replace" => false,
            "error" => true,
            _ => throw new ArgumentException(
                $"Неизвестный режим 'invalidEncoding' ('{value}'). Допустимые: replace, error.")
        };
    }

    private static Encoding ResolveEncoding(string? name, bool throwOnInvalid)
    {
        string? normalized = string.IsNullOrWhiteSpace(name) ? null : name.Trim();
        if (normalized is null
            || normalized.Equals("utf-8", StringComparison.OrdinalIgnoreCase)
            || normalized.Equals("utf8", StringComparison.OrdinalIgnoreCase))
        {
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: throwOnInvalid);
        }

        try
        {
            return Encoding.GetEncoding(
                normalized,
                EncoderFallback.ReplacementFallback,
                throwOnInvalid ? DecoderFallback.ExceptionFallback : DecoderFallback.ReplacementFallback);
        }
        catch (ArgumentException exception)
        {
            throw new ArgumentException($"Неподдерживаемая кодировка '{normalized}': {exception.Message}");
        }
    }
}
