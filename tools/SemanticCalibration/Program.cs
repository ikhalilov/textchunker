using System.Globalization;
using System.Text.Json;
using TextChunker.Abstractions;
using TextChunker.Abstractions.Embeddings;
using TextChunker.Chunking.Semantic;
using TextChunker.Embeddings;
using TextChunker.Tokenizers;

namespace TextChunker.Tools.SemanticCalibration;

/// <summary>
/// Computes boundary F1 for semantic chunking on a labeled corpus.
/// Each document has a nearby <c>*.json</c> with substrings that start a
/// new semantic segment. The tool sweeps percentiles and windows and prints
/// a precision/recall/F1 table so the defaults can be calibrated.
/// </summary>
internal static class Program
{
    private static double _tolerance = 1.0;

    private static readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);

    private static async Task<int> Main(string[] args)
    {
        var options = CliOptions.Parse(args);
        string corpus = options.Get("--corpus") ?? "tests/calibration/corpus";
        string endpoint = options.Get("--endpoint") ?? "http://127.0.0.1:9932";
        string? model = options.Get("--model") ?? "embeddinggemma-2";
        string cacheDir = options.Get("--cache")
            ?? Path.Combine(Path.GetTempPath(), "textchunker-calibration-cache");
        double[] percentiles = ParseDoubles(options.Get("--percentiles"), [0.75, 0.80, 0.85, 0.90, 0.95]);
        int[] windows = ParseInts(options.Get("--windows"), [1, 2, 3, 5]);
        double[] minDistances = ParseDoubles(
            options.Get("--min-distances"),
            new[] { new SemanticOptions().MinDistance });
        int concurrency = ParseInt(options.Get("--concurrency"), 1);
        int batchSize = ParseInt(options.Get("--batch-size"), 32);
        int maxAtomTokens = ParseInt(options.Get("--max-atom-tokens"), new SemanticOptions().MaxAtomTokens);
        _tolerance = ParseDouble(options.Get("--tolerance"), 1.0);
        bool verbose = options.Has("--verbose");

        if (!Directory.Exists(corpus))
        {
            Console.Error.WriteLine($"Corpus not found: {corpus}");
            return 2;
        }

        List<Document> documents = LoadCorpus(corpus);
        if (documents.Count == 0)
        {
            Console.Error.WriteLine($"No *.md + *.json pairs found in the corpus: {corpus}");
            return 2;
        }

        IEmbeddingProvider provider = EmbeddingProviderFactory.Create(new EmbeddingOptions
        {
            Provider = "llama-cpp",
            Endpoint = endpoint,
            Model = model,
            Dimensions = 768,
            BatchSize = batchSize,
            Concurrency = concurrency,
            UseCache = true,
            CacheDir = cacheDir
        });

        var tokenizer = new WordTokenizer();
        var defaults = new SemanticOptions() with { MaxAtomTokens = maxAtomTokens };
        Console.WriteLine($"Corpus: {corpus} ({documents.Count} docs)");
        Console.WriteLine($"Endpoint: {endpoint}, model: {model ?? "(default)"}, " +
            $"batch={batchSize}, concurrency={concurrency}, maxAtomTokens={maxAtomTokens}");
        Console.WriteLine($"Boundary match tolerance: ±{_tolerance:0.#} atoms");
        Console.WriteLine();

        if (options.Has("--dump-atoms"))
        {
            DumpAtoms(documents, tokenizer, provider, defaults with
            {
                Percentile = percentiles[0],
                Window = windows[0]
            });
        }

        var rows = new List<Row>();
        foreach (double percentile in percentiles)
        {
            foreach (int window in windows)
            {
                foreach (double minDistance in minDistances)
                {
                    var semantic = defaults with
                    {
                        Percentile = percentile,
                        Window = window,
                        MinDistance = minDistance
                    };
                    semantic.Validate();

                    var total = new Counters();
                    int breakpoints = 0;
                    int atoms = 0;
                    int evaluated = 0;
                    var perDocument = new List<string>();

                    foreach (Document document in documents)
                    {
                        IReadOnlyList<SemanticAtom> atomList = SemanticAtomizer.Atomize(
                            document.Text, tokenizer, semantic);
                        int[] truth = MapLabels(document, atomList);
                        if (truth.Length == 0)
                        {
                            perDocument.Add($"{document.Name}: no labeled boundaries");
                            continue;
                        }

                        evaluated++;
                        var planner = new SemanticPlanner(provider, semantic);
                        SemanticPlan plan = await planner.PlanAsync(document.Text, tokenizer);
                        Counters counters = Count(truth, plan.Breakpoints.ToArray());
                        total.Add(counters);
                        breakpoints += plan.Breakpoints.Count;
                        atoms += atomList.Count;

                        if (verbose)
                        {
                            perDocument.Add(
                                $"{document.Name}: P={counters.Precision:0.00} R={counters.Recall:0.00} " +
                                $"F1={counters.F1:0.00} (pred=[{string.Join(",", plan.Breakpoints)}], " +
                                $"truth=[{string.Join(",", truth)}], atoms={atomList.Count})");
                        }
                    }

                    int truthCount = total.TruePositives + total.FalseNegatives;
                    rows.Add(new Row(
                        percentile, window, minDistance, total, breakpoints, atoms,
                        perDocument, evaluated, truthCount));
                }
            }
        }

        Print(rows, verbose);
        return rows.Count == 0 ? 1 : 0;
    }

    private static void DumpAtoms(
        IReadOnlyList<Document> documents,
        ITokenizer tokenizer,
        IEmbeddingProvider provider,
        SemanticOptions semantic)
    {
        foreach (Document document in documents)
        {
            IReadOnlyList<SemanticAtom> atoms = SemanticAtomizer.Atomize(document.Text, tokenizer, semantic);
            int[] truth = MapLabels(document, atoms);
            SemanticPlan plan = new SemanticPlanner(provider, semantic)
                .PlanAsync(document.Text, tokenizer)
                .GetAwaiter()
                .GetResult();

            Console.WriteLine($"=== {document.Name} (atoms={atoms.Count}) ===");
            for (int i = 0; i < atoms.Count; i++)
            {
                string marker = (truth.Contains(i) ? "T" : " ") + (plan.StartsNewChunk(i) ? "B" : " ");
                string distance = i > 0 && i - 1 < plan.Distances.Count
                    ? plan.Distances[i - 1].ToString("0.000", CultureInfo.InvariantCulture)
                    : "  -  ";
                string preview = atoms[i].Text.Replace("\n", "\\n", StringComparison.Ordinal);
                if (preview.Length > 64)
                {
                    preview = preview[..64] + "…";
                }
                Console.WriteLine($"{i,3} {marker} d_before={distance} {preview}");
            }
            Console.WriteLine();
        }
    }

    private static int[] MapLabels(Document document, IReadOnlyList<SemanticAtom> atoms)    {
        var indexes = new List<int>();
        foreach (string label in document.Boundaries)
        {
            int offset = document.Text.IndexOf(label, StringComparison.Ordinal);
            if (offset < 0)
            {
                Console.Error.WriteLine($"{document.Name}: label not found: '{label}'");
                continue;
            }

            int atom = -1;
            for (int i = 0; i < atoms.Count; i++)
            {
                if (atoms[i].Start <= offset && offset < atoms[i].End)
                {
                    atom = i;
                    break;
                }
            }

            if (atom <= 0)
            {
                // 0 is the start of the document and is not a boundary.
                Console.Error.WriteLine($"{document.Name}: label '{label}' falls into the first atom or outside the atoms");
                continue;
            }

            if (!indexes.Contains(atom))
            {
                indexes.Add(atom);
            }
        }

        indexes.Sort();
        return [.. indexes];
    }

    /// <summary>Greedy matching with a tolerance: each true boundary looks for an unused prediction.</summary>
    private static Counters Count(int[] truth, int[] predicted)
    {
        var used = new bool[predicted.Length];
        int truePositives = 0;
        foreach (int expected in truth)
        {
            for (int i = 0; i < predicted.Length; i++)
            {
                if (!used[i] && Math.Abs(predicted[i] - expected) <= _tolerance)
                {
                    used[i] = true;
                    truePositives++;
                    break;
                }
            }
        }

        int falsePositives = predicted.Length - truePositives;
        int falseNegatives = truth.Length - truePositives;
        return new Counters(truePositives, falsePositives, falseNegatives);
    }

    private static List<Document> LoadCorpus(string root)
    {
        var documents = new List<Document>();
        foreach (string path in Directory.GetFiles(root, "*.md", SearchOption.AllDirectories).OrderBy(p => p))
        {
            string labelPath = Path.ChangeExtension(path, ".json");
            if (!File.Exists(labelPath))
            {
                continue;
            }

            var labels = JsonSerializer.Deserialize<LabelFile>(File.ReadAllText(labelPath), _options);
            if (labels?.Boundaries is not { Length: > 0 })
            {
                continue;
            }

            documents.Add(new Document(
                Path.GetRelativePath(root, path),
                File.ReadAllText(path),
                labels.Boundaries));
        }

        return documents;
    }

    private static void Print(IReadOnlyList<Row> rows, bool verbose)
    {
        if (verbose)
        {
            foreach (Row row in rows)
            {
                Console.WriteLine(
                    $"--- percentile={row.Percentile:0.00} window={row.Window} " +
                    $"minDistance={row.MinDistance:0.###} ---");
                foreach (string line in row.PerDocument)
                {
                    Console.WriteLine($"  {line}");
                }
            }
            Console.WriteLine();
        }

        Console.WriteLine("| percentile | window | minDist | precision | recall | F1 | pred/doc | truth/doc | atoms/doc |");
        Console.WriteLine("|---:|---:|---:|---:|---:|---:|---:|---:|---:|");
        foreach (Row row in rows.OrderByDescending(r => r.Counters.F1))
        {
            Console.WriteLine(
                $"| {row.Percentile:0.00} | {row.Window} | {row.MinDistance:0.###} | " +
                $"{row.Counters.Precision:0.000} | " +
                $"{row.Counters.Recall:0.000} | {row.Counters.F1:0.000} | " +
                $"{row.Breakpoints / (double)Math.Max(1, row.DocumentCount):0.0} | " +
                $"{row.Truth / (double)Math.Max(1, row.DocumentCount):0.0} | " +
                $"{row.Atoms / (double)Math.Max(1, row.DocumentCount):0.0} |");
        }
    }

    private static double[] ParseDoubles(string? value, double[] fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => double.Parse(v.Trim(), CultureInfo.InvariantCulture))];

    private static int[] ParseInts(string? value, int[] fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(v => int.Parse(v.Trim(), CultureInfo.InvariantCulture))];

    private static int ParseInt(string? value, int fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : int.Parse(value.Trim(), CultureInfo.InvariantCulture);

    private static double ParseDouble(string? value, double fallback)
        => string.IsNullOrWhiteSpace(value)
            ? fallback
            : double.Parse(value.Trim(), CultureInfo.InvariantCulture);

    private sealed record Document(string Name, string Text, string[] Boundaries);

    private sealed record LabelFile(string[]? Boundaries);

    private sealed record Row(
        double Percentile,
        int Window,
        double MinDistance,
        Counters Counters,
        int Breakpoints,
        int Atoms,
        List<string> PerDocument,
        int DocumentCount,
        int Truth);

    private sealed class Counters
    {
        public Counters()
        {
        }

        public Counters(int truePositives, int falsePositives, int falseNegatives)
        {
            TruePositives = truePositives;
            FalsePositives = falsePositives;
            FalseNegatives = falseNegatives;
        }

        public int TruePositives { get; private set; }
        public int FalsePositives { get; private set; }
        public int FalseNegatives { get; private set; }

        public double Precision => Divide(TruePositives, TruePositives + FalsePositives);
        public double Recall => Divide(TruePositives, TruePositives + FalseNegatives);
        public double F1 => Precision + Recall <= 0 ? 0 : 2 * Precision * Recall / (Precision + Recall);

        public void Add(Counters other)
        {
            TruePositives += other.TruePositives;
            FalsePositives += other.FalsePositives;
            FalseNegatives += other.FalseNegatives;
        }

        private static double Divide(int numerator, int denominator)
            => denominator == 0 ? 0 : (double)numerator / denominator;
    }

    private sealed class CliOptions
    {
        private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _flags = new(StringComparer.OrdinalIgnoreCase);

        public static CliOptions Parse(string[] args)
        {
            var options = new CliOptions();
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                if (!arg.StartsWith("--", StringComparison.Ordinal))
                {
                    continue;
                }

                if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                {
                    options._values[arg] = args[++i];
                }
                else
                {
                    options._flags.Add(arg);
                }
            }
            return options;
        }

        public string? Get(string name) => _values.TryGetValue(name, out string? value) ? value : null;

        public bool Has(string name) => _flags.Contains(name);
    }
}
