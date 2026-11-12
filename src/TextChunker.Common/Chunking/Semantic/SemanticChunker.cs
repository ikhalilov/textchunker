using TextChunker.Abstractions;

namespace TextChunker.Chunking.Semantic;

/// <summary>
/// Strategy that needs a pre-built <see cref="SemanticPlan"/>:
/// <see cref="ChunkingService"/> builds the plan asynchronously (with embeddings)
/// and passes it here before calling <see cref="IChunker.Chunk"/>.
/// </summary>
public interface ISemanticPlanAware
{
    void AttachPlan(SemanticPlan plan);
}

/// <summary>
/// Semantic chunking: gathers atoms into chunks so that a chunk ends at
/// a meaningful boundary and always fits the limit of the active unit.
/// Boundaries come from <see cref="SemanticPlan"/>; a group is also closed when the
/// limit is exceeded. An atom that does not fit the limit by itself is split hard at token boundaries.
/// Overlap is collected from whole atoms, so sentences are never cut.
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

        // Semantic offsets refer to the whole document: nothing is cut before EOF,
        // otherwise atom boundaries would not match the current (partial) buffer.
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
            // A new semantic section or exceeding the limit closes the group.
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

    /// <summary>Text from the start of atom <paramref name="from"/> to the end of atom <paramref name="to"/>.</summary>
    private static string Span(string text, IReadOnlyList<SemanticAtom> atoms, int from, int to)
    {
        int start = atoms[from].Start;
        int end = atoms[to].End;
        return end <= start ? string.Empty : text[start..end];
    }

    /// <summary>
    /// Start of the next group: the tail of the previous one, collected from whole atoms
    /// within the overlap. Always advances forward (not less than <paramref name="nextAtom"/>).
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

        // Overlap is collected from whole atoms; if the entire group fits into the overlap,
        // it is repeated in full (standard overlap semantics). Progress is guaranteed:
        // the loop continues from the next atom.
        return candidateStart;
    }

    /// <summary>Emits a group; if it does not fit, applies a hard split by the limit.</summary>
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
    /// A hard cut is aligned to a word boundary: if the last whitespace before <paramref name="cut"/>
    /// cuts off at least half of the piece, the cut is made there — otherwise fragments like "во"/"ду." remain.
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
                // Do not go past the limits: the right edge of the whitespace remains a separator.
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
