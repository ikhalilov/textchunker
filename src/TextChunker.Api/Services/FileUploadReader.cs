using System.Text;

namespace TextChunker.Api.Services;

/// <summary>
/// Reading an uploaded `.txt`/`.md`/`.markdown` file: extension and size checks,
/// encoding (UTF-8 by default, code pages), BOM, and invalid-encoding mode.
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
            throw new ArgumentException("No file was provided, or the file is empty.");
        }

        string extension = Path.GetExtension(file.FileName);
        if (!AllowedExtensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
        {
            throw new UnsupportedMediaTypeException(
                $"Only .txt, .md, .markdown are supported (received '{extension}').");
        }

        long maxBytes = (long)maxTextChars * 4 + 4;
        if (file.Length > maxBytes)
        {
            throw new PayloadTooLargeException($"File exceeds the limit ({maxBytes} bytes).");
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
            throw new ArgumentException("The file is empty.");
        }
        if (text.Length > maxTextChars)
        {
            throw new PayloadTooLargeException($"Text exceeds the limit of {maxTextChars} characters.");
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
                $"Unknown 'invalidEncoding' mode ('{value}'). Allowed values: replace, error.")
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
            throw new ArgumentException($"Unsupported encoding '{normalized}': {exception.Message}");
        }
    }
}
