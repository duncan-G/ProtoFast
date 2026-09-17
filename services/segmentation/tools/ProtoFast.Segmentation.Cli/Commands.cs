using System.Text.Json;
using ProtoFast.Segmentation.Core.Evaluation;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Cli;

/// <summary>Runs the deterministic phases and writes the artifacts a real run would produce.</summary>
public static class SegmentCommand
{
    public static async Task<int> RunAsync(Dictionary<string, string> options)
    {
        var (markdown, layout) = await DocumentFiles.ReadAsync(
            options.Require("in"), options.TryGetValue("layout", out var l) ? l : null);

        var run = DeterministicPipeline.Run(markdown, layout);
        var outDir = options.TryGetValue("out", out var dir) ? dir : "segctl-out";
        Directory.CreateDirectory(outDir);

        // The same artifact names a real run writes to S3 (plan §9.1), so a local run and a
        // production run can be diffed against each other directly.
        await WriteLinesAsync(Path.Combine(outDir, "00_lines.jsonl"), run.Extraction.Lines);
        await WriteAsync(Path.Combine(outDir, "00_stats.json"), run.Extraction.Statistics);
        await WriteLinesAsync(Path.Combine(outDir, "01_clean.jsonl"), run.Cleaning.Lines);
        await WriteAsync(Path.Combine(outDir, "01_boundaries.json"), run.Cleaning.Boundaries);
        await WriteAsync(Path.Combine(outDir, "02_triage.json"), run.Triage);
        await WriteAsync(Path.Combine(outDir, "03_labels_merged.json"), run.Labels);
        await WriteLinesAsync(Path.Combine(outDir, "04_paragraphs.jsonl"), run.Assembly.Paragraphs);
        await WriteAsync(Path.Combine(outDir, "05_tree.json"), run.Root);
        await WriteAsync(Path.Combine(outDir, "06_validation.json"), run.Validation);

        if (!options.Has("quiet"))
        {
            Console.WriteLine($"lines        {run.Extraction.Lines.Count}");
            Console.WriteLine($"family       {run.Extraction.DocumentFamily}");
            Console.WriteLine($"condition    {run.Triage.Condition}");
            Console.WriteLine($"artifacts    {run.Cleaning.ArtifactLineIds.Count} removed");
            Console.WriteLine($"boundaries   {run.Cleaning.Boundaries.Count} trusted");
            Console.WriteLine($"suspect      {run.Triage.SuspectRegions.Count} regions"
                + (run.Triage.CanSkipLabeling ? " (labelling would be skipped)" : " (labelling would run)"));
            Console.WriteLine($"paragraphs   {run.Assembly.Paragraphs.Count}");
            Console.WriteLine($"headings     {run.Assembly.Headings.Count}");
            Console.WriteLine($"sections     {run.Root.Descend().Count()}");
            Console.WriteLine($"validation   {(run.Validation.Passed ? "passed" : "FAILED")}");
            Console.WriteLine();
            Console.WriteLine($"artifacts written to {Path.GetFullPath(outDir)}");
        }

        return run.Validation.Passed ? 0 : 1;
    }

    private static Task WriteAsync<T>(string path, T value) =>
        File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, Cli.Json));

    private static Task WriteLinesAsync<T>(string path, IEnumerable<T> values) =>
        File.WriteAllLinesAsync(
            path,
            values.Select(v => JsonSerializer.Serialize(
                v, new JsonSerializerOptions(JsonSerializerDefaults.Web))));
}

/// <summary>Runs every deterministic check and prints the report.</summary>
public static class ChecksCommand
{
    public static async Task<int> RunAsync(Dictionary<string, string> options)
    {
        var (markdown, layout) = await DocumentFiles.ReadAsync(
            options.Require("in"), options.TryGetValue("layout", out var l) ? l : null);

        var run = DeterministicPipeline.Run(markdown, layout);

        if (options.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(run.Validation, Cli.Json));
            return run.Validation.Passed ? 0 : 1;
        }

        foreach (var result in run.Validation.Results)
        {
            Console.WriteLine($"{(result.Passed ? "pass" : "FAIL")}  {result.CheckId}");

            foreach (var error in result.Errors.Take(10))
            {
                Console.WriteLine($"        {error}");
            }

            if (result.Errors.Count > 10)
            {
                Console.WriteLine($"        …and {result.Errors.Count - 10} more");
            }
        }

        return run.Validation.Passed ? 0 : 1;
    }
}

/// <summary>Scores the deterministic pipeline against gold documents (plan §26).</summary>
public static class EvaluateCommand
{
    public static async Task<int> RunAsync(Dictionary<string, string> options)
    {
        var goldPath = options.Require("gold");

        var files = Directory.Exists(goldPath)
            ? Directory.GetFiles(goldPath, "*.json", SearchOption.AllDirectories).Order().ToArray()
            : [goldPath];

        if (files.Length == 0)
        {
            Console.Error.WriteLine($"error: no gold documents under '{goldPath}'");
            return 2;
        }

        var metrics = new List<DocumentMetrics>();

        foreach (var file in files)
        {
            var gold = JsonSerializer.Deserialize<GoldDocument>(
                await File.ReadAllTextAsync(file),
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
                });

            if (gold is null)
            {
                Console.Error.WriteLine($"warning: '{file}' did not parse as a gold document; skipping");
                continue;
            }

            var run = DeterministicPipeline.Run(gold.Markdown, gold.Layout);

            metrics.Add(GoldEvaluator.Evaluate(gold, new EvaluatedRun(
                run.Assembly.Paragraphs,
                run.Assembly.Headings,
                run.Root,
                run.Cleaning.Lines
                    .Where(line => run.Cleaning.ArtifactLineIds.Contains(line.LineId))
                    .Select(line => line.Text)
                    .ToHashSet(StringComparer.Ordinal),
                run.Validation,
                CostUsd: 0,
                Duration: TimeSpan.Zero)));
        }

        if (options.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(metrics, Cli.Json));
        }
        else
        {
            Report(metrics);
        }

        // A baseline that cannot fail proves nothing (plan §6): the deterministic pipeline is
        // expected to miss thresholds on the degraded bucket, and a non-zero exit says so.
        var failures = metrics.Count(m => !m.Passes(AcceptanceThresholds.For(m.Condition)));
        return failures == 0 ? 0 : 1;
    }

    private static void Report(IReadOnlyList<DocumentMetrics> metrics)
    {
        foreach (var bucket in metrics.GroupBy(m => m.Condition).OrderBy(g => g.Key))
        {
            var thresholds = AcceptanceThresholds.For(bucket.Key);
            var passed = bucket.Count(m => m.Passes(thresholds));

            Console.WriteLine($"== {bucket.Key} ({bucket.Count()} documents, {passed} passing)");
            Console.WriteLine($"   Pk               {Mean(bucket, m => m.Pk):0.000}  (≤ {thresholds.MaxPk:0.00})");
            Console.WriteLine($"   WindowDiff       {Mean(bucket, m => m.WindowDiff):0.000}  (≤ {thresholds.MaxWindowDiff:0.00})");
            Console.WriteLine($"   Boundary F1 ±1   {Mean(bucket, m => m.BoundaryF1Tolerant.F1):0.000}");
            Console.WriteLine($"   Heading F1       {Mean(bucket, m => m.HeadingF1.F1):0.000}  (≥ {thresholds.MinHeadingF1:0.00})");
            Console.WriteLine($"   Tree distance    {Mean(bucket, m => m.TreeEditDistance):0.000}  (≤ {thresholds.MaxTreeEditDistance:0.00})");
            Console.WriteLine($"   Artifact recall  {Mean(bucket, m => m.ArtifactRemoval.Recall):0.000}  (≥ {thresholds.MinArtifactRecall:0.00})");
            Console.WriteLine($"   Text integrity   {bucket.Count(m => m.TextIntegrityPassed)}/{bucket.Count()}");
            Console.WriteLine();
        }

        // The worst documents are what anyone tuning a rule actually wants to look at (plan §26.3).
        var worst = metrics.OrderByDescending(m => m.Pk).Take(10).ToList();
        if (worst.Count > 0)
        {
            Console.WriteLine("worst documents by Pk:");
            foreach (var m in worst)
            {
                Console.WriteLine(
                    $"   {m.Pk:0.000}  {m.DocumentId}  ({m.Condition}, heading F1 {m.HeadingF1.F1:0.00})");
            }
        }
    }

    private static double Mean(IEnumerable<DocumentMetrics> metrics, Func<DocumentMetrics, double> selector)
    {
        var values = metrics.Select(selector).Where(v => !double.IsNaN(v)).ToList();
        return values.Count == 0 ? 0 : values.Average();
    }
}
