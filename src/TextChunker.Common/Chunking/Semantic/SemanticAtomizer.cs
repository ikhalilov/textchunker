using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;

namespace TextChunker.Chunking.Semantic;

/// <summary>
/// Разбивает текст на атомы — единицы, которые семантическая нарезка считает неделимыми:
/// блоки кода (целиком), заголовки Markdown, элементы списков, абзацы и предложения.
/// Защита от сокращений (<c>т. д.</c>, <c>см.</c>, <c>Mr.</c>, инициалы) не даёт
/// резать предложения на них. Все смещения — UTF-16 индексы в исходном тексте;
/// атомы упорядочены, не пересекаются, а промежутки между ними — разделители.
/// </summary>
public static class SemanticAtomizer
{
    /// <summary>Сокращения, после которых точка не начинает новое предложение (нижний регистр, без точки).</summary>
    private static readonly HashSet<string> Abbreviations = new(StringComparer.Ordinal)
    {
        // русские
        "т", "д", "п", "е", "г", "др", "см", "стр", "рис", "табл", "напр", "м", "ож",
        "тел", "факс", "проф", "доц", "акад", "член-корр", "ул", "пер", "просп", "бул",
        "обл", "р-н", "гор", "кв", "комн", "руб", "коп", "млн", "млрд", "тыс",
        "ст", "вв", "э", "гг", "т.е", "т.к", "т.н", "т.о", "т.п", "т.д", "пр",
        "ж", "муж", "раз", "экз", "шт", "цен", "№", "г.до н.э", "до н.э", "н.э",
        // английские
        "mr", "mrs", "ms", "dr", "prof", "st", "ave", "blvd", "rd", "vs", "etc", "inc",
        "ltd", "co", "corp", "fig", "eq", "no", "vol", "pp", "al", "ca", "approx",
        "dept", "est", "lb", "op", "cit", "p", "ed", "eds", "trans"
    };

    private const char Ellipsis = '…';

    private static readonly HashSet<char> Terminators = ['.', '!', '?', Ellipsis];

    private static readonly HashSet<char> ClosingPairs =
    [
        '"', '\'', '»', '“', '’', ')', ']', '}', '>'
    ];

    public static IReadOnlyList<SemanticAtom> Atomize(
        string text,
        ITokenizer tokenizer,
        SemanticOptions options)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokenizer);
        ArgumentNullException.ThrowIfNull(options);
        if (text.Length == 0)
        {
            return [];
        }

        List<Line> lines = EnumerateLines(text);
        List<SemanticAtom> atoms = new();

        int paragraphStart = -1;
        int paragraphEnd = -1;
        int listStart = -1;
        int listEnd = -1;
        char? fence = null;
        int fenceStart = -1;

        void FlushParagraph()
        {
            if (paragraphStart >= 0)
            {
                atoms.AddRange(SplitSentences(text, paragraphStart, paragraphEnd));
                paragraphStart = -1;
                paragraphEnd = -1;
            }
        }

        void FlushListItem()
        {
            if (listStart >= 0)
            {
                atoms.Add(MakeAtom(text, listStart, listEnd, SemanticAtomKind.ListItem));
                listStart = -1;
                listEnd = -1;
            }
        }

        foreach (Line line in lines)
        {
            string content = text.Substring(line.Start, line.End - line.Start);
            string trimmed = content.Trim();

            // 1. Блоки кода: ограждение открывает и закрывает, внутри ничего не режем.
            if (fence is null && TryFenceMarker(trimmed, out char fenceChar))
            {
                FlushParagraph();
                FlushListItem();
                fence = fenceChar;
                fenceStart = line.Start;
                continue;
            }

            if (fence is not null)
            {
                if (IsClosingFence(trimmed, fence.Value))
                {
                    atoms.Add(MakeAtom(text, fenceStart, line.End, SemanticAtomKind.Code));
                    fence = null;
                    fenceStart = -1;
                }
                continue;
            }

            // 2. Пустая строка завершает абзац и элемент списка.
            if (trimmed.Length == 0)
            {
                FlushParagraph();
                FlushListItem();
                continue;
            }

            // 3. Заголовок — отдельный атом.
            if (IsHeading(trimmed))
            {
                FlushParagraph();
                FlushListItem();
                atoms.Add(MakeAtom(text, line.Start, line.End, SemanticAtomKind.Heading));
                continue;
            }

            // 4. Элемент списка: новая строка-маркер начинает новый атом,
            //    отступленная продолжение-строка дописывается в текущий.
            if (IsListItem(trimmed))
            {
                FlushParagraph();
                FlushListItem();
                listStart = line.Start;
                listEnd = line.End;
                continue;
            }

            if (listStart >= 0)
            {
                if (content.Length > 0 && (content[0] == ' ' || content[0] == '\t'))
                {
                    listEnd = line.End;
                    continue;
                }
                FlushListItem();
            }

            // 5. Обычная строка абзаца.
            if (paragraphStart < 0)
            {
                paragraphStart = line.Start;
            }
            paragraphEnd = line.End;
        }

        // Незакрытое ограждение — атом до конца текста.
        if (fence is not null)
        {
            atoms.Add(MakeAtom(text, fenceStart, text.Length, SemanticAtomKind.Code));
        }

        FlushParagraph();
        FlushListItem();

        return PostProcess(atoms, text, tokenizer, options);
    }

    /// <summary>Склейка коротких атомов и дробление слишком длинных.</summary>
    private static IReadOnlyList<SemanticAtom> PostProcess(
        List<SemanticAtom> atoms,
        string text,
        ITokenizer tokenizer,
        SemanticOptions options)
    {
        List<SemanticAtom> merged = MergeShort(atoms, text, options);
        List<SemanticAtom> result = new(merged.Count);

        foreach (SemanticAtom atom in merged)
        {
            if (atom.Kind == SemanticAtomKind.Code || atom.Kind == SemanticAtomKind.Heading)
            {
                result.Add(atom);
                continue;
            }

            if (tokenizer.CountTokens(atom.Text) <= options.MaxAtomTokens)
            {
                result.Add(atom);
                continue;
            }

            result.AddRange(SplitOversized(atom, tokenizer, options.MaxAtomTokens));
        }

        return result;
    }

    private static List<SemanticAtom> MergeShort(List<SemanticAtom> atoms, string text, SemanticOptions options)
    {
        if (atoms.Count < 2 || options.MinAtomChars <= 0)
        {
            return [.. atoms];
        }

        var result = new List<SemanticAtom>(atoms.Count);
        int i = 0;
        while (i < atoms.Count)
        {
            SemanticAtom atom = atoms[i];
            if (!IsMergeable(atom) || atom.Text.Length >= options.MinAtomChars)
            {
                result.Add(atom);
                i++;
                continue;
            }

            // Короткий фрагмент в начале абзаца — это, как правило, подзаголовок
            // («Охрана труда») или лид: приклеиваем к нему следующее предложение,
            // чтобы граница темы осталась в начале атома.
            bool paragraphStart = IsParagraphStart(text, atom.Start);
            int start = atom.Start;
            int end = atom.End;
            int next = i + 1;

            if (next < atoms.Count
                && IsMergeable(atoms[next])
                && (paragraphStart || atoms[next].Text.Length < options.MinAtomChars))
            {
                end = atoms[next].End;
                next++;
            }

            // Дальше присоединяем только короткие атомы, пока не наберём минимум:
            // длинное предложение внутрь короткого фрагмента не затягиваем.
            while (next < atoms.Count
                   && IsMergeable(atoms[next])
                   && atoms[next].Text.Length < options.MinAtomChars
                   && end - start < options.MinAtomChars)
            {
                end = atoms[next].End;
                next++;
            }

            if (next > i + 1)
            {
                result.Add(MakeAtom(text, start, end, atom.Kind));
                i = next;
                continue;
            }

            // Соседей нет: приклеиваемся к предыдущему короткому, иначе оставляем
            // отдельным (короткий заголовок в конце абзаца важнее не потерять).
            if (result.Count > 0
                && IsMergeable(result[result.Count - 1])
                && result[result.Count - 1].Text.Length < options.MinAtomChars)
            {
                SemanticAtom previous = result[result.Count - 1];
                result[result.Count - 1] = MakeAtom(text, previous.Start, atom.End, previous.Kind);
            }
            else
            {
                result.Add(atom);
            }
            i++;
        }

        return result;
    }

    private static bool IsMergeable(SemanticAtom atom)
        => atom.Kind is SemanticAtomKind.Sentence or SemanticAtomKind.Text;

    /// <summary>Атом начинается с новой строки после пустой строки (или с начала текста).</summary>
    private static bool IsParagraphStart(string text, int start)
        => start == 0
           || (start >= 2 && text[start - 1] == '\n' && text[start - 2] == '\n');

    /// <summary>Жёсткий дробление атома по границам токенов (суррогаты не рвутся).</summary>
    private static IEnumerable<SemanticAtom> SplitOversized(SemanticAtom atom, ITokenizer tokenizer, int maxTokens)
    {
        string body = atom.Text;
        int relative = 0;
        while (relative < body.Length)
        {
            int remaining = tokenizer.CountTokens(body.Substring(relative));
            if (remaining <= maxTokens)
            {
                yield return MakeAtom(body, relative, body.Length, atom.Kind, atom.Start);
                yield break;
            }

            int cut = tokenizer.CutAtTokenBoundary(body.Substring(relative), maxTokens) ?? maxTokens;
            if (cut <= 0)
            {
                cut = 1;
            }

            yield return MakeAtom(body, relative, relative + cut, atom.Kind, atom.Start);
            relative += cut;
        }
    }

    /// <summary>Разбиение диапазона на предложения с защитой от сокращений.</summary>
    private static IEnumerable<SemanticAtom> SplitSentences(string text, int start, int end)
    {
        int sentenceStart = IndexOfNonWhitespace(text, start, end);
        if (sentenceStart < 0)
        {
            yield break;
        }

        int i = sentenceStart;
        while (i < end)
        {
            char c = text[i];
            if (!Terminators.Contains(c))
            {
                i++;
                continue;
            }

            int terminatorEnd = i + 1;
            if (c == '.' && i + 2 < end && text[i + 1] == '.' && text[i + 2] == '.')
            {
                terminatorEnd = i + 3; // «...”
            }

            // «1.5» — десятичная дробь, не граница.
            if (c == '.'
                && terminatorEnd < end
                && char.IsDigit(text[terminatorEnd])
                && i > 0
                && char.IsDigit(text[i - 1]))
            {
                i = terminatorEnd;
                continue;
            }

            int afterTerminator = terminatorEnd;
            while (afterTerminator < end && ClosingPairs.Contains(text[afterTerminator]))
            {
                afterTerminator++;
            }

            bool atEnd = afterTerminator >= end;
            bool followedBySpace = !atEnd && char.IsWhiteSpace(text[afterTerminator]);
            if (!atEnd && !followedBySpace)
            {
                i = terminatorEnd;
                continue;
            }

            if (IsAbbreviation(text, sentenceStart, i))
            {
                i = terminatorEnd;
                continue;
            }

            // Закрывающие кавычки/скобки после терминатора входят в предложение.
            yield return MakeAtom(text, sentenceStart, afterTerminator, SemanticAtomKind.Sentence);

            int next = IndexOfNonWhitespace(text, afterTerminator, end);
            if (next < 0)
            {
                yield break;
            }
            sentenceStart = next;
            i = next;
        }

        int tail = IndexOfNonWhitespace(text, sentenceStart, end);
        if (tail >= 0 && tail < end)
        {
            int tailEnd = LastIndexOfNonWhitespace(text, tail, end) + 1;
            if (tailEnd > tail)
            {
                yield return MakeAtom(text, tail, tailEnd, SemanticAtomKind.Sentence);
            }
        }
    }

    /// <summary>Слово перед терминатором — сокращение или инициал?</summary>
    private static bool IsAbbreviation(string text, int sentenceStart, int terminatorIndex)
    {
        int wordEnd = terminatorIndex;
        int wordStart = wordEnd;
        while (wordStart > sentenceStart && IsWordChar(text[wordStart - 1]))
        {
            wordStart--;
        }

        if (wordStart >= wordEnd)
        {
            return false;
        }

        string word = text.Substring(wordStart, wordEnd - wordStart).ToLowerInvariant();

        // Инициалы («И.», «А.С.») и однобуквенные сокращения не режем;
        // одиночная цифра («раздел 2.») — наоборот, обычная граница.
        if (word.Length == 1 && char.IsLetter(word[0]))
        {
            return true;
        }

        return Abbreviations.Contains(word);
    }

    private static bool IsWordChar(char c)
    {
        return char.IsLetterOrDigit(c) || c == '-' || c == '\'' || c == '’';
    }

    private static int IndexOfNonWhitespace(string text, int start, int end)
    {
        for (int i = start; i < end; i++)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }
        return -1;
    }

    private static int LastIndexOfNonWhitespace(string text, int start, int end)
    {
        for (int i = end - 1; i >= start; i--)
        {
            if (!char.IsWhiteSpace(text[i]))
            {
                return i;
            }
        }
        return start - 1;
    }

    /// <summary>Обрезанный атом с поправкой смещений на срезанные пробелы.</summary>
    private static SemanticAtom MakeAtom(string source, int start, int end, SemanticAtomKind kind, int offset = 0)
    {
        int from = start;
        int to = end;
        while (from < to && char.IsWhiteSpace(source[from]))
        {
            from++;
        }
        while (to > from && char.IsWhiteSpace(source[to - 1]))
        {
            to--;
        }
        return new SemanticAtom(source.Substring(from, to - from), offset + from, offset + to, kind);
    }

    private static bool TryFenceMarker(string trimmed, out char fenceChar)
    {
        fenceChar = '\0';
        if (trimmed.Length < 3)
        {
            return false;
        }
        char c = trimmed[0];
        if (c != '`' && c != '~')
        {
            return false;
        }
        int count = 0;
        while (count < trimmed.Length && trimmed[count] == c)
        {
            count++;
        }
        if (count < 3)
        {
            return false;
        }
        fenceChar = c;
        return true;
    }

    private static bool IsClosingFence(string trimmed, char fenceChar)
    {
        if (trimmed.Length == 0 || trimmed[0] != fenceChar)
        {
            return false;
        }
        int count = 0;
        while (count < trimmed.Length && trimmed[count] == fenceChar)
        {
            count++;
        }
        return count >= 3 && trimmed.Substring(count).Trim().Length == 0;
    }

    private static bool IsHeading(string trimmed)
    {
        if (trimmed.Length == 0 || trimmed[0] != '#')
        {
            return false;
        }
        int level = 0;
        while (level < trimmed.Length && trimmed[level] == '#')
        {
            level++;
        }
        return level is >= 1 and <= 6 && (level >= trimmed.Length || trimmed[level] == ' ' || trimmed[level] == '\t');
    }

    private static bool IsListItem(string trimmed)
    {
        if (trimmed.Length == 0)
        {
            return false;
        }
        if (trimmed[0] is '-' or '*' or '+')
        {
            return trimmed.Length == 1 || trimmed[1] == ' ' || trimmed[1] == '\t';
        }

        int i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i]) && i < 4)
        {
            i++;
        }
        if (i == 0 || i >= trimmed.Length)
        {
            return false;
        }
        return (trimmed[i] == '.' || trimmed[i] == ')')
            && i + 1 < trimmed.Length
            && (trimmed[i + 1] == ' ' || trimmed[i + 1] == '\t');
    }

    private static List<Line> EnumerateLines(string text)
    {
        var lines = new List<Line>();
        int start = 0;
        for (int i = 0; i < text.Length; i++)
        {
            if (text[i] == '\n')
            {
                lines.Add(new Line(start, i));
                start = i + 1;
            }
        }
        if (start <= text.Length)
        {
            lines.Add(new Line(start, text.Length));
        }
        return lines;
    }

    /// <summary><see cref="End"/> — исключая перевод строки.</summary>
    private readonly record struct Line(int Start, int End);
}
