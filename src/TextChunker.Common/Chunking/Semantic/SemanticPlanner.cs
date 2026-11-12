using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;

namespace TextChunker.Chunking.Semantic;

/// <summary>Строит план семантических границ для куска текста.</summary>
public interface ISemanticPlanner
{
    Task<SemanticPlan> PlanAsync(
        string text,
        ITokenizer tokenizer,
        CancellationToken cancellationToken = default);
}

/// <summary>
/// Планировщик семантических границ: атомизация → эмбеддинги (батчи + кэш внутри
/// провайдера) → попарные расстояния <c>1 − cos</c> (опционально окна TextTiling) →
/// границы там, где расстояние не ниже перцентиля и не меньше <c>minDistance</c>.
/// </summary>
public sealed class SemanticPlanner : ISemanticPlanner
{
    private const double Epsilon = 1e-9;

    private readonly IEmbeddingProvider _provider;
    private readonly SemanticOptions _options;

    public SemanticPlanner(IEmbeddingProvider provider, SemanticOptions options)
    {
        _provider = provider ?? throw new ArgumentNullException(nameof(provider));
        _options = options ?? throw new ArgumentNullException(nameof(options));
    }

    public async Task<SemanticPlan> PlanAsync(
        string text,
        ITokenizer tokenizer,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(tokenizer);

        IReadOnlyList<SemanticAtom> atoms = SemanticAtomizer.Atomize(text, tokenizer, _options);
        if (atoms.Count == 0)
        {
            return SemanticPlan.Empty(_provider.Name);
        }

        var atomTexts = new string[atoms.Count];
        for (int i = 0; i < atoms.Count; i++)
        {
            atomTexts[i] = atoms[i].Text;
        }

        IReadOnlyList<float[]> vectors = await _provider.EmbedAsync(atomTexts, cancellationToken).ConfigureAwait(false);
        ValidateVectors(vectors, atoms.Count);

        int dimensions = vectors[0].Length;
        double[] distances = ComputeDistances(vectors, dimensions);
        List<int> breakpoints = SelectBreakpoints(distances);
        AddHeadingBreakpoints(atoms, breakpoints);
        string? model = _provider is IEmbeddingModelInfo info ? info.ModelName : null;
        var cacheStats = _provider as IEmbeddingCacheStats;

        return new SemanticPlan(
            atoms,
            distances,
            breakpoints,
            _provider.Name,
            model,
            dimensions,
            cacheStats?.CacheHits,
            cacheStats?.CacheMisses);
    }

    private void ValidateVectors(IReadOnlyList<float[]> vectors, int expected)
    {
        if (vectors.Count != expected)
        {
            throw new EmbeddingProviderException(
                $"Провайдер {_provider.Name} вернул {vectors.Count} эмбеддингов для {expected} атомов.");
        }

        int dimension = -1;
        for (int i = 0; i < vectors.Count; i++)
        {
            float[] vector = vectors[i];
            if (vector is null || vector.Length == 0)
            {
                throw new EmbeddingProviderException($"Эмбеддинг атома #{i} пуст.");
            }
            if (dimension < 0)
            {
                dimension = vector.Length;
            }
            else if (vector.Length != dimension)
            {
                throw new EmbeddingProviderException(
                    $"Эмбеддинги разной размерности: #{i} имеет {vector.Length}, ожидалось {dimension}.");
            }
        }
    }

    /// <summary>
    /// Расстояния между соседями. При <c>Window &gt; 1</c> сравниваются средние векторы
    /// левого и правого окон (TextTiling) — это устойчивее к шуму отдельных предложений.
    /// </summary>
    private double[] ComputeDistances(IReadOnlyList<float[]> vectors, int dimensions)
    {
        int gaps = vectors.Count - 1;
        var distances = new double[gaps];
        if (gaps <= 0)
        {
            return distances;
        }

        int window = Math.Max(1, _options.Window);
        for (int gap = 0; gap < gaps; gap++)
        {
            if (window == 1)
            {
                distances[gap] = 1.0 - Cosine(vectors[gap], vectors[gap + 1]);
                continue;
            }

            float[] left = MeanVector(vectors, Math.Max(0, gap - window + 1), gap + 1, dimensions);
            float[] right = MeanVector(vectors, gap + 1, Math.Min(vectors.Count, gap + 1 + window), dimensions);
            distances[gap] = 1.0 - Cosine(left, right);
        }

        return distances;
    }

    private static float[] MeanVector(IReadOnlyList<float[]> vectors, int from, int to, int dimensions)
    {
        var mean = new float[dimensions];
        int count = Math.Max(1, to - from);
        for (int i = from; i < to; i++)
        {
            float[] vector = vectors[i];
            for (int d = 0; d < dimensions; d++)
            {
                mean[d] += vector[d] / count;
            }
        }
        return Normalize(mean);
    }

    private static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (float value in vector)
        {
            sum += (double)value * value;
        }

        double norm = Math.Sqrt(sum);
        if (norm <= Epsilon)
        {
            return vector;
        }

        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
        return vector;
    }

    private static double Cosine(float[] left, float[] right)
    {
        int length = Math.Min(left.Length, right.Length);
        double sum = 0;
        for (int i = 0; i < length; i++)
        {
            sum += (double)left[i] * right[i];
        }
        return sum;
    }

    /// <summary>
    /// Границы: расстояние не ниже перцентиля (nearest-rank) и не меньше minDistance.
    /// Если все расстояния одинаковы, выделять нечего — границ не ставим.
    /// </summary>
    private List<int> SelectBreakpoints(IReadOnlyList<double> distances)
    {
        var breakpoints = new List<int>();
        if (distances.Count == 0)
        {
            return breakpoints;
        }

        double min = distances.Min();
        double max = distances.Max();
        if (max - min <= Epsilon)
        {
            return breakpoints;
        }

        double threshold = Percentile(distances, _options.Percentile);
        for (int gap = 0; gap < distances.Count; gap++)
        {
            if (distances[gap] >= threshold - Epsilon && distances[gap] >= _options.MinDistance - Epsilon)
            {
                breakpoints.Add(gap + 1);
            }
        }

        return breakpoints;
    }

    /// <summary>
    /// Markdown-заголовки — структурные границы: чанк должен начинаться с заголовка,
    /// даже если эмбеддинг короткого заголовка близок к соседнему тексту.
    /// </summary>
    private static void AddHeadingBreakpoints(IReadOnlyList<SemanticAtom> atoms, List<int> breakpoints)
    {
        bool added = false;
        for (int i = 1; i < atoms.Count; i++)
        {
            if (atoms[i].Kind == SemanticAtomKind.Heading && !breakpoints.Contains(i))
            {
                breakpoints.Add(i);
                added = true;
            }
        }

        if (added)
        {
            breakpoints.Sort();
        }
    }

    /// <summary>Перцентиль методом nearest-rank: <c>ceil(p·n)</c>-й по возрастанию.</summary>
    internal static double Percentile(IReadOnlyList<double> values, double percentile)
    {
        if (values.Count == 0)
        {
            return 0;
        }

        double[] sorted = values.ToArray();
        Array.Sort(sorted);

        int rank = (int)Math.Ceiling(percentile * sorted.Length);
        if (rank < 1)
        {
            rank = 1;
        }
        if (rank > sorted.Length)
        {
            rank = sorted.Length;
        }
        return sorted[rank - 1];
    }
}
