using System.Text;
using System.Text.RegularExpressions;

using TextChunker.Abstractions;

namespace TextChunker.Sources;

/// <summary>Общий конструктор StreamReader с BOM-обработкой и итератор строк в нормализованном пространстве.</summary>
internal static class TextLines
{
    private static readonly Regex WhitespaceCollapse = new(@"[ \t]+", RegexOptions.CultureInvariant);
    private static bool _codePagesRegistered;

    /// <summary>Идиопотентная регистрация CodePages-провайдера (windows-1251, koi8-r, iso-8859-1 и др.).</summary>
    public static void EnsureCodePages()
    {
        lock (typeof(TextLines))
        {
            if (!_codePagesRegistered)
            {
                Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
                _codePagesRegistered = true;
            }
        }
    }

    /// <summary>
    /// StreamReader над источником: первые 4 байта читаются заранее; UTF-8 BOM (EF BB BF)
    /// отбрасывается (только для UTF-8), дальше ConcatStream(head, stream).
    /// </summary>
    public static StreamReader CreateReader(Stream stream, TextSourceOptions opts, out bool bom)
    {
        EnsureCodePages();

        byte[] headBuf = new byte[4];
        int headLen = stream.Read(headBuf);
        bool isUtf8 = IsUtf8Name(opts.EncodingName);
        bool bomDetected = isUtf8 && headLen >= 3
            && headBuf[0] == 0xEF && headBuf[1] == 0xBB && headBuf[2] == 0xBF;
        bom = bomDetected;

        byte[] head = new byte[Math.Max(0, headLen - (bomDetected ? 3 : 0))];
        Array.Copy(headBuf, bomDetected ? 3 : 0, head, 0, headLen - (bomDetected ? 3 : 0));

        Stream combined = new ConcatStream(new MemoryStream(head, writable: false), stream);
        var enc = CreateEncoding(opts.EncodingName, throwOnInvalid: isUtf8 && opts.InvalidAsError);
        // BOM уже отброшен вручную; auto-detection выключена; буфер 4096.
        return new StreamReader(combined, enc, false, 4096);
    }

    private static Encoding CreateEncoding(string? name, bool throwOnInvalid)
    {
        if (IsUtf8Name(name))
        {
            // UTF8Encoding(encoderShouldEmitUTF8Identifier, throwOnInvalidBytes):
            // throwOnInvalidBytes=false — замена невалидных последовательностей на U+FFFD;
            // throwOnInvalidBytes=true — DecoderFallbackException → exit 4.
            return new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: throwOnInvalid);
        }

        try
        {
            return Encoding.GetEncoding((name ?? "windows-1251").ToLowerInvariant());
        }
        catch (ArgumentException ex)
        {
            throw new TextSourceErrorException(2, $"Неподдерживаемая кодировка '{name}': {ex.Message}");
        }
    }

    private static bool IsUtf8Name(string? name)
    {
        if (string.IsNullOrEmpty(name)) return true;
        var n = name.ToLowerInvariant();
        return n is "utf-8" or "utf8" or "unicode" or "utf-16le" or "utf-16be"
            or "unicode-little-endian" or "unicode-big-endian";
    }

    /// <summary>
    /// Итерация по строкам: (normLine, startChar, endChar). startChar/endChar — в пространстве
    /// join(content_i, "\n"); endChar = startChar + normLine.Length (exclusive).
    /// </summary>
    public static IEnumerable<(string NormLine, long StartChar, long EndChar)>
        ReadLines(TextReader reader, TextSourceOptions opts)
    {
        long pos = 0;
        while (true)
        {
            string? line;
            try
            {
                line = reader.ReadLine();
            }
            catch (DecoderFallbackException ex)
            {
                throw new TextSourceErrorException(4, $"Невалидная UTF-8: {ex.Message}");
            }
            if (line is null)
            {
                break;
            }
            string normLine = NormalizeLine(line, opts);
            long startChar = pos;
            long endChar = pos + normLine.Length;
            pos = endChar + 1; // +1 — за \n (line ending)
            yield return (normLine, startChar, endChar);
        }
    }

    private static string NormalizeLine(string line, TextSourceOptions opts)
    {
        string result = line;

        if (opts.NormalizeNfc || opts.NormalizeNfkc)
        {
            // NFKC более сильный; если оба — NFKC.
            var form = opts.NormalizeNfkc ? NormalizationForm.FormKC : NormalizationForm.FormC;
            result = result.Normalize(form);
        }

        if (opts.NormalizeWhitespace)
        {
            result = WhitespaceCollapse.Replace(result, " ");
            result = result.TrimEnd(' ');
        }

        return result;
    }
}
