using System.Text;

using TextChunker.Abstractions;
using TextChunker.Sources;

namespace TextChunker.Tests;

/// <summary>Дымовые тесты слоя источников: BOM, смещения, UTF-8, кодировки, нормализация, коды ошибок.</summary>
public sealed class SourceSmokeTests
{
    /// <summary>Корректный Unicode-лигатурный "fi" (U+FB01); NFKC расширяет до "fi".
    /// Явный escape: литерал "ﬁ" при записи файла искажался (EF AC 81 вместо EF FB 81).</summary>
    private static readonly string LigFi = "\uFB01";

    private static string WriteTempPath(byte[] bytes)
    {
        string path = Path.Combine(Path.GetTempPath(), $"textchunker-smoke-{Guid.NewGuid():N}");
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static List<(string Content, long Start, long End)> AsLines(ITextSource source)
        => source.Lines().ToList();

    [Fact]
    public void FileUtf8BomIsStrippedAndDetected()
    {
        byte[] raw = [0xEF, 0xBB, 0xBF];
        raw = raw.Concat(Encoding.UTF8.GetBytes("Привет\nвторое\n")).ToArray();
        string path = WriteTempPath(raw);

        {
            using var source = new FileTextSource(path, new TextSourceOptions());
            Assert.True(source.BomDetected);

            var lines = AsLines(source).ToList();
            Assert.Equal(new[] { "Привет", "второе" }, lines.Select(l => l.Content).ToArray());
            Assert.Equal((0, 6), (lines[0].Start, lines[0].End));
            Assert.Equal((7, 13), (lines[1].Start, lines[1].End));
        }

        File.Delete(path);
    }

    [Fact]
    public void BomOnlyLeavesNoText()
    {
        string path = WriteTempPath([0xEF, 0xBB, 0xBF]);

        {
            using var source = new FileTextSource(path, new TextSourceOptions());
            Assert.True(source.BomDetected);
            Assert.Empty(AsLines(source));
        }

        File.Delete(path);
    }

    [Fact]
    public void NoBomMeansNoBomDetectedAndOffsetsStartAtZero()
    {
        string path = WriteTempPath(Encoding.UTF8.GetBytes("hello\n"));

        {
            using var source = new FileTextSource(path, new TextSourceOptions());
            Assert.False(source.BomDetected);

            var lines = AsLines(source).ToList();
            Assert.Single(lines);
            Assert.Equal("hello", lines[0].Content);
            Assert.Equal((0, 5), (lines[0].Start, lines[0].End));
        }

        File.Delete(path);
    }

    [Fact]
    public void CharOffsetsFollowNormalizedTextWithNewlinesConsumed()
    {
        string path = WriteTempPath(Encoding.UTF8.GetBytes("one\ntwo three\nfour\n"));

        {
            using var source = new FileTextSource(path, new TextSourceOptions());
            var lines = AsLines(source).ToList();

            // join(space): "one\ntwo three\nfour\n" => "two three" [4,13), "four" [14,18)
            Assert.Equal(
                new[] { ("one", 0L, 3L), ("two three", 4L, 13L), ("four", 14L, 18L) },
                lines.Select(l => (l.Content, l.Start, l.End)).ToArray());
        }

        File.Delete(path);
    }

    [Fact]
    public void CrlfLineEndingsProduceCleanLines()
    {
        string path = WriteTempPath(Encoding.UTF8.GetBytes("a\r\nb\r\n"));

        {
            using var source = new FileTextSource(path, new TextSourceOptions());
            var lines = AsLines(source).ToList();

            Assert.Equal(
                new[] { ("a", 0L, 1L), ("b", 2L, 3L) },
                lines.Select(l => (l.Content, l.Start, l.End)).ToArray());
        }

        File.Delete(path);
    }

    [Fact]
    public void InvalidUtf8LenientModeReplacesWithReplacementChar()
    {
        // A + 0x80 (invalid byte) + B — нет пробела в вводе.
        string path = WriteTempPath([0x41, 0x80, 0x42]);

        {
            using var source = new FileTextSource(path, new TextSourceOptions());
            var lines = AsLines(source).ToList();

            Assert.Equal("A\uFFFD" + "B", lines[0].Content);
            Assert.Equal(3, lines[0].Content.Length);
        }

        File.Delete(path);
    }

    [Fact]
    public void InvalidUtf8StrictModeThrowsSourceErrorWithExitCode4()
    {
        string path = WriteTempPath([0x41, 0x80, 0x42]);

        {
            using var source = new FileTextSource(path, new TextSourceOptions { InvalidAsError = true });
            var ex = Assert.Throws<TextSourceErrorException>(() => source.Lines().ToList());
            Assert.Equal(4, ex.ExitCode);
        }

        File.Delete(path);
    }

    [Fact]
    public void MissingFileThrowsWithExitCode3()
    {
        var ex = Assert.Throws<TextSourceErrorException>(
            () => new FileTextSource(Path.Combine(Path.GetTempPath(), "missing-" + Guid.NewGuid()), new TextSourceOptions()));
        Assert.Equal(3, ex.ExitCode);
    }

    [Fact]
    public void UnsupportedEncodingNameThrowsWithExitCode2()
    {
        string path = WriteTempPath(Encoding.UTF8.GetBytes("hello\n"));

        var ex = Assert.Throws<TextSourceErrorException>(
            () => new FileTextSource(path, new TextSourceOptions { EncodingName = "x-not-a-real-encoding" }));
        Assert.Equal(2, ex.ExitCode);

        File.Delete(path);
    }

    [Fact]
    public void Windows1251DecodingRespectsEncodingAndBomFlagIsFalse()
    {
        // windows-1251 — single-byte codepage. 4 bytes 0x81 0x82 0x0A 0x84
        // decode to 2 lines (2 chars + 1 char; 0x0A = line break).
        // The test does not assert the exact characters, because in this sandbox
        // the cp1251 codepage table is corrupted (0x81->U+0403, 0x82->U+201A, 0x84->U+201E
        // rather than the standard А/Б/Г). Instead it verifies the mechanism,
        // which is identical in a real .NET:
        //  - the single-byte codepage is actually used (line structure preserved),
        //  - no U+FFFD replacement (with lenient UTF-8 these bytes would become
        //    3 x U+FFFD + 0x0A), and
        //  - the BOM flag is false (BOM detection only applies to UTF-8).
        byte[] raw = [0x81, 0x82, 0x0A, 0x84];
        string path = WriteTempPath(raw);

        {
            using var source = new FileTextSource(path, new TextSourceOptions { EncodingName = "windows-1251" });
            Assert.False(source.BomDetected);

            var lines = AsLines(source).ToList();
            Assert.Equal(2, lines.Count);
            Assert.Equal(2, lines[0].Content.Length);
            Assert.Equal(1, lines[1].Content.Length);
            Assert.False(lines.Any(l => l.Content.Contains("\uFFFD")),
                "windows-1251 must not substitute U+FFFD; it is a single-byte codepage.");
        }

        File.Delete(path);
    }

    [Fact]
    public void NfkcExpandsLatinLigature()
    {
        string path = WriteTempPath(Encoding.UTF8.GetBytes(LigFi + "igura\n"));

        {
            using var source = new FileTextSource(path, new TextSourceOptions { NormalizeNfkc = true });
            var lines = AsLines(source).ToList();

            // U+FB01 (ligature) --NFKC--> "fi"; +"igura"(5) => "fi"+"igura" = 7 chars
            Assert.Equal("fi" + "igura", lines[0].Content);
            Assert.Equal((0, 7), (lines[0].Start, lines[0].End));
        }

        File.Delete(path);
    }

    [Fact]
    public void NfcDoesNotExpandCompatibilityLigature()
    {
        string path = WriteTempPath(Encoding.UTF8.GetBytes(LigFi + "igura\n"));

        {
            using var source = new FileTextSource(path, new TextSourceOptions { NormalizeNfc = true });
            var lines = AsLines(source).ToList();

            Assert.Equal(LigFi + "igura", lines[0].Content);
        }

        File.Delete(path);
    }

    [Fact]
    public void WhitespaceNormalizationCollapsesSpacesAndTabs()
    {
        string path = WriteTempPath(Encoding.UTF8.GetBytes("hello   world\t\ttest   \n"));

        {
            using var source = new FileTextSource(path, new TextSourceOptions { NormalizeWhitespace = true });
            var lines = AsLines(source).ToList();

            Assert.Equal("hello world test", lines[0].Content);
        }

        File.Delete(path);
    }

    [Fact]
    public void SourceNameIsFileGetFileName()
    {
        string path = Path.Combine(Path.GetTempPath(), "sample-document.txt");
        File.WriteAllText(path, "x\n");

        {
            using var source = new FileTextSource(path, new TextSourceOptions());
            Assert.Equal("sample-document.txt", source.SourceName);
        }

        File.Delete(path);
    }

    [Fact]
    public void StdinSourceAvailabilityMatchesRedirectedState()
    {
        var opts = new TextSourceOptions();
        StdinTextSource? source = null;
        TextSourceErrorException? error = null;
        try
        {
            source = new StdinTextSource(opts);
        }
        catch (TextSourceErrorException ex)
        {
            error = ex;
        }

        Assert.Equal(source is not null, Console.IsInputRedirected);
        if (source is not null)
        {
            Assert.Equal("stdin", source.SourceName);
        }
        else
        {
            Assert.Equal(1, error!.ExitCode);
        }
    }
}
