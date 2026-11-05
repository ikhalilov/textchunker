using TextChunker.Abstractions;

namespace TextChunker.Chunking.Semantic;

/// <summary>
/// Стратегия, которой нужен заранее построенный <see cref="SemanticPlan"/>:
/// <see cref="ChunkingService"/> строит план асинхронно (с эмбеддингами)
/// и передаёт его сюда перед вызовом <see cref="IChunker.Chunk"/>.
/// </summary>
public interface ISemanticPlanAware
{
    void AttachPlan(SemanticPlan plan);
}

/// <summary>
/// Семантическая нарезка: собирает атомы в чанки так, чтобы чанк заканчивался на
/// осмысленной границе и гарантированно влезал в лимит активной единицы.
/// Границы берутся из <see cref="SemanticPlan"/>; группа закрывается и при переполнении
/// лимита. Атом, который сам не влезает в лимит, дробится жёстко по границам токенов.
/// Перекрытие набирается целыми атомами, поэтому предложения не рвутся.
/// </summary>
public sealed class SemanticChunker : IChunker, ISemanticPlanAware
{
    private SemanticPlan? _plan;

    public void AttachPlan(SemanticPlan plan)
        => _plan = plan ?? throw new ArgumentNullException(nameof(plan));

    public ChunkSplit Chunk(string text, ITokenizer tokenizer, ChunkingOptions options, bool final = false)
    {
        SemanticPlan plan = _plan ?? throw new InvalidOperationException(
            "Семантический план не построен: вызовите AttachPlan перед Chunk.");

        if (string.IsNullOrEmpty(text))
        {
            return ChunkSplit.Empty;
        }

        // Семантические смещения относятся ко всему документу: до EOF ничего не режем,
        // иначе границы атомов не совпадут с текущим (частичным) буфером.
        if (!final)
        {
            return ChunkSplit.NeedMore;
        }

        var measure = new SizeMeasure(tokenizer, options.Unit, options.Prefix);
        int max = options.MaxSize ?? int.MaxValue;
        int overlap = ChunkerSupport.EffectiveOverlap(options);
        IReadOnlyList<SemanticAtom> atoms = plan.Atoms;

        var segments = new List<ChunkSegment>();
        if (atoms.Count == 0)
        {
            return new ChunkSplit { Segments = segments, ConsumedEnd = text.Length, MoreNeeded = false };
        }

        int groupStart = 0;
        int groupEnd = 0;

        for (int i = 1; i < atoms.Count; i++)
        {
            // Новая семантическая секция или переполнение лимита закрывают группу.
            bool startsSection = plan.StartsNewChunk(i);
            bool fits = !startsSection && measure.Count(Span(text, atoms, groupStart, i)) <= max;
            if (fits)
            {
                groupEnd = i;
                continue;
            }

            EmitGroup(text, atoms, groupStart, groupEnd, segments, measure, max, options.Trim);
            groupStart = NextGroupStart(text, atoms, groupStart, groupEnd, overlap, measure, i);
            groupEnd = groupStart;
        }

        EmitGroup(text, atoms, groupStart, groupEnd, segments, measure, max, options.Trim);

        return new ChunkSplit
        {
            Segments = segments,
            ConsumedEnd = text.Length,
            MoreNeeded = false
        };
    }

    /// <summary>Текст от начала атома <paramref name="from"/> до конца атома <paramref name="to"/>.</summary>
    private static string Span(string text, IReadOnlyList<SemanticAtom> atoms, int from, int to)
    {
        int start = atoms[from].Start;
        int end = atoms[to].End;
        return end <= start ? string.Empty : text[start..end];
    }

    /// <summary>
    /// Начало следующей группы: хвост предыдущей, набранный целыми атомами
    /// в пределах перекрытия. Всегда продвигает вперёд (не меньше <paramref name="nextAtom"/>).
    /// </summary>
    private static int NextGroupStart(
        string text,
        IReadOnlyList<SemanticAtom> atoms,
        int groupStart,
        int groupEnd,
        int overlap,
        SizeMeasure measure,
        int nextAtom)
    {
        if (overlap <= 0 || groupEnd <= groupStart)
        {
            return nextAtom;
        }

        int candidateStart = groupEnd;
        for (int candidate = groupEnd - 1; candidate >= groupStart; candidate--)
        {
            int end = atoms[groupEnd].End;
            int size = measure.Count(text.Substring(atoms[candidate].Start, end - atoms[candidate].Start));
            if (size > overlap)
            {
                break;
            }
            candidateStart = candidate;
        }

        // Перекрытие набирается целыми атомами; если в overlap помещается вся группа,
        // повторяем её целиком (стандартная семантика overlap). Прогресс гарантирован:
        // цикл продолжается со следующего атома.
        return candidateStart;
    }

    /// <summary>Выдача группы; если она не влезает — жёсткий дробление по лимиту.</summary>
    private static void EmitGroup(
        string text,
        IReadOnlyList<SemanticAtom> atoms,
        int fromAtom,
        int toAtom,
        List<ChunkSegment> segments,
        SizeMeasure measure,
        int max,
        bool trim)
    {
        int start = atoms[fromAtom].Start;
        int end = atoms[toAtom].End;
        if (end <= start)
        {
            return;
        }

        if (measure.Count(text.Substring(start, end - start)) <= max)
        {
            ChunkerSupport.AddSegment(segments, text, start, end, trim);
            return;
        }

        int pos = start;
        while (pos < end)
        {
            int cut = measure.MaxCut(text, pos, max);
            if (cut <= pos)
            {
                cut = SizeMeasure.NextCodePoint(text, pos, end);
            }
            if (cut > end)
            {
                cut = end;
            }

            cut = PreferWordBoundary(text, pos, cut, end);

            ChunkerSupport.AddSegment(segments, text, pos, cut, trim);
            pos = cut;
        }
    }

    /// <summary>
    /// Жёсткий рез выравниваем на границу слова: если последний пробел перед <paramref name="cut"/>
    /// отрезает не меньше половины куска, режем по нему — иначе остаются обрывки вроде «во»/«ду.».
    /// </summary>
    private static int PreferWordBoundary(string text, int start, int cut, int end)
    {
        if (cut >= end || cut <= start)
        {
            return cut;
        }

        int minimum = start + (cut - start) / 2;
        for (int i = cut - 1; i > minimum; i--)
        {
            if (char.IsWhiteSpace(text[i]))
            {
                int boundary = i + 1;
                // Не выходим за пределы: правый край пробелов остаётся разделителем.
                while (boundary < cut && char.IsWhiteSpace(text[boundary]))
                {
                    boundary++;
                }
                return boundary <= cut ? boundary : cut;
            }
        }

        return cut;
    }
}
