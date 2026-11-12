using System.Text;

using TextChunker.Abstractions.Embeddings;

namespace TextChunker.Embeddings;

/// <summary>
/// Deterministic offline provider: the hashing trick over words and character n-grams.
/// It does not provide the semantic quality of embeddinggemma, but it is stable across runs and machines,
/// so it is used in unit/CI tests and as a fallback without a GPU.
/// </summary>
public sealed class HashEmbeddingProvider : IEmbeddingProvider
{
    private const ulong FnvOffsetBasis = 14695981039346656037UL;
    private const ulong FnvPrime = 1099511628211UL;
    private const int GramSize = 3;

    private readonly int _dimensions;

    public HashEmbeddingProvider() : this(768)
    {
    }

    public HashEmbeddingProvider(int dimensions)
    {
        if (dimensions < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(dimensions), dimensions, "Размерность должна быть не меньше 1.");
        }

        _dimensions = dimensions;
    }

    public string Name => "hash";

    public int Dimensions => _dimensions;

    public Task<IReadOnlyList<float[]>> EmbedAsync(
        IReadOnlyList<string> texts,
        CancellationToken cancellationToken = default)
    {
        if (texts is null)
        {
            throw new ArgumentNullException(nameof(texts));
        }

        var vectors = new float[texts.Count][];
        for (int i = 0; i < texts.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            vectors[i] = Embed(texts[i]);
        }

        return Task.FromResult<IReadOnlyList<float[]>>(vectors);
    }

    /// <summary>Vector of a single text: L2-normalized, deterministic with respect to the content.</summary>
    public float[] Embed(string text)
    {
        float[] vector = new float[_dimensions];
        if (string.IsNullOrEmpty(text))
        {
            vector[0] = 1f;
            return vector;
        }

        string normalized = text.Trim().ToLowerInvariant();

        foreach (string word in Words(normalized))
        {
            Accumulate(vector, word, weight: 1f);
        }

        foreach (string gram in Grams(normalized))
        {
            Accumulate(vector, gram, weight: 0.5f);
        }

        return Normalize(vector);
    }

    private static IEnumerable<string> Words(string text)
    {
        int start = -1;
        for (int i = 0; i <= text.Length; i++)
        {
            bool isWordChar = i < text.Length && !char.IsWhiteSpace(text[i]) && !IsPunctuation(text[i]);
            if (isWordChar)
            {
                if (start < 0)
                {
                    start = i;
                }
                continue;
            }

            if (start >= 0)
            {
                yield return text.Substring(start, i - start);
                start = -1;
            }
        }
    }

    private static IEnumerable<string> Grams(string text)
    {
        string compact = Compact(text);
        if (compact.Length < GramSize)
        {
            if (compact.Length > 0)
            {
                yield return compact;
            }
            yield break;
        }

        for (int i = 0; i + GramSize <= compact.Length; i++)
        {
            yield return compact.Substring(i, GramSize);
        }
    }

    private static string Compact(string text)
    {
        var builder = new StringBuilder(text.Length);
        foreach (char c in text)
        {
            if (!char.IsWhiteSpace(c) && !IsPunctuation(c))
            {
                builder.Append(c);
            }
        }
        return builder.ToString();
    }

    private static bool IsPunctuation(char c)
    {
        return char.IsPunctuation(c) || char.IsSymbol(c);
    }

    private void Accumulate(float[] vector, string feature, float weight)
    {
        ulong hash = Fnv1a(feature);
        int index = (int)(hash % (ulong)_dimensions);
        float sign = (hash & 1UL) == 0UL ? weight : -weight;
        vector[index] += sign;
    }

    private static ulong Fnv1a(string value)
    {
        ulong hash = FnvOffsetBasis;
        foreach (char c in value)
        {
            hash ^= c;
            hash *= FnvPrime;
        }
        return hash;
    }

    internal static float[] Normalize(float[] vector)
    {
        double sum = 0;
        foreach (float value in vector)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
            {
                throw new EmbeddingProviderException("Эмбеддинг содержит NaN/Infinity.");
            }
            sum += (double)value * value;
        }

        double norm = Math.Sqrt(sum);
        if (norm == 0)
        {
            vector[0] = 1f;
            return vector;
        }

        for (int i = 0; i < vector.Length; i++)
        {
            vector[i] = (float)(vector[i] / norm);
        }
        return vector;
    }
}
