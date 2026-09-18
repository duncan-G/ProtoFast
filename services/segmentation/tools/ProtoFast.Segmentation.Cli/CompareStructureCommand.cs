using System.Text.Json;
using ProtoFast.Segmentation.Core.Assembly;
using ProtoFast.Segmentation.Core.Evaluation;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Core.Windowing;
using ProtoFast.Segmentation.Pipeline.Executors;

namespace ProtoFast.Segmentation.Cli;

/// <summary>One strategy's numbers for one document.</summary>
public sealed record StructureComparison(
    string DocumentId,
    string Strategy,
    string? ModelKey,
    double TreeEditDistance,
    int Sections,
    int MaxDepth,
    int InferredTitles,
    int CrossWindowSections,
    bool TreeSchemaPassed);

/// <summary>
/// Scores two phase-5 trees for the same document against its gold annotation — the A/B of
/// orchestrator plan §9.
///
/// <para>It takes trees rather than running them, and that is deliberate. Producing a tree needs a
/// provider, a budget ledger and a database; <c>segctl</c>'s promise is that everything it does
/// runs from a file with no keys. So the worker produces the two trees — the same document, run
/// twice with <c>Seg_Pipeline__Structure__Strategy</c> set each way — and this scores them. Cost
/// and wall clock are the other half of the decision and live in <c>model_calls</c>, keyed by run
/// id; they are not in the tree artifact and are not invented here.</para>
///
/// <para><see cref="StructureComparison.CrossWindowSections"/> is the measurement the experiment
/// exists for. The chunked splice structurally cannot produce a section whose paragraphs span a
/// window cut: each part is unwrapped and concatenated, and nothing ever reasons across the
/// boundary. A non-zero count for the orchestrated strategy is cross-boundary reasoning that
/// happened; whether it was <em>right</em> is what the edit distance beside it says.</para>
/// </summary>
public static class CompareStructureCommand
{
    public static async Task<int> RunAsync(Dictionary<string, string> options)
    {
        var gold = await ReadGoldAsync(options.Require("gold"));
        var run = DeterministicPipeline.Run(gold.Markdown, gold.Layout);

        var cuts = WindowCuts(run.Assembly.Paragraphs, run.Assembly.Headings);
        var rows = new List<StructureComparison>();

        foreach (var (name, path) in Trees(options))
        {
            var artifact = await ReadTreeAsync(path);

            rows.Add(Compare(gold, run, artifact, name, cuts));
        }

        if (rows.Count == 0)
        {
            Console.Error.WriteLine("error: pass at least one of --chunked or --orchestrated");
            return 2;
        }

        if (options.Has("json"))
        {
            Console.WriteLine(JsonSerializer.Serialize(rows, Cli.Json));
            return 0;
        }

        Report(gold, rows, cuts.Count);
        return 0;
    }

    private static IEnumerable<(string Name, string Path)> Trees(Dictionary<string, string> options)
    {
        if (options.TryGetValue("chunked", out var chunked) && chunked != "true")
        {
            yield return ("chunked", chunked);
        }

        if (options.TryGetValue("orchestrated", out var orchestrated) && orchestrated != "true")
        {
            yield return ("orchestrated", orchestrated);
        }
    }

    private static StructureComparison Compare(
        GoldDocument gold,
        DeterministicRunResult run,
        TreeArtifact artifact,
        string name,
        IReadOnlySet<string> cuts)
    {
        var metrics = GoldEvaluator.Evaluate(gold, new EvaluatedRun(
            run.Assembly.Paragraphs,
            run.Assembly.Headings,
            artifact.Root,
            run.Cleaning.Lines
                .Where(line => run.Cleaning.ArtifactLineIds.Contains(line.LineId))
                .Select(line => line.Text)
                .ToHashSet(StringComparer.Ordinal),
            Report(artifact.Root, run),
            CostUsd: 0,
            Duration: TimeSpan.Zero));

        var sections = artifact.Root.Descend().ToList();
        var recorded = artifact.Strategy.ToString().ToLowerInvariant();

        return new StructureComparison(
            gold.DocumentId,
            // The artifact records which strategy produced it, so a tree handed to the wrong flag
            // says so rather than being silently compared against itself.
            recorded == name ? name : $"{name}!={recorded}",
            artifact.ModelKey,
            metrics.TreeEditDistance,
            sections.Count,
            sections.Max(s => s.Level),
            sections.Count(s => s.TitleInferred),
            sections.Count(s => CrossesACut(s, cuts)),
            metrics.TreeSchemaPassed);
    }

    /// <summary>
    /// A section whose paragraphs sit on both sides of a window cut. The splice cannot make one;
    /// the orchestrator can, and each is a place where a section the windows saw as two was put
    /// back together.
    /// </summary>
    private static bool CrossesACut(SectionNode node, IReadOnlySet<string> cuts) =>
        node.ParagraphIds.Count > 1
        && node.ParagraphIds.Skip(1).Any(cuts.Contains);

    /// <summary>
    /// The first committed entry of every window but the first — the exact points the chunked
    /// strategy splices at, computed over the same planner both strategies use.
    /// </summary>
    private static IReadOnlySet<string> WindowCuts(
        IReadOnlyList<Paragraph> paragraphs, IReadOnlyList<HeadingRecord> headings)
    {
        var entries = SkeletonBuilder.Build(paragraphs, headings);

        return StructureWindowPlanner.Plan(entries, headings)
            .Skip(1)
            .SelectMany(w => w.CommitEntries.Where(e => !e.IsHeading).Take(1).Select(e => e.Id))
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>
    /// The structural checks, re-run over this tree. The run's own report describes the
    /// deterministic tree it built, which is not the one being scored — but its text-integrity
    /// result is carried over unchanged, because that check is about the assembly both strategies
    /// share and neither of them can affect it.
    /// </summary>
    private static ValidationReport Report(SectionNode root, DeterministicRunResult run) =>
        new(
        [
            run.Validation.Results.First(r => r.CheckId == Checks.TextIntegrity),
            Checks.CheckTreeShape(root),
            Checks.CheckContiguity(root, run.Assembly.Paragraphs),
            Checks.CheckHeadingAnchor(
                root, run.Assembly.Headings.Select(h => h.LineId).ToHashSet(StringComparer.Ordinal)),
        ]);

    private static void Report(GoldDocument gold, IReadOnlyList<StructureComparison> rows, int cuts)
    {
        Console.WriteLine($"== {gold.DocumentId}  ({gold.Condition}, {gold.Family}, {cuts} window cuts)");
        Console.WriteLine();
        Console.WriteLine("  strategy       tree dist   sections   depth   inferred   cross-window   shape");

        foreach (var row in rows)
        {
            Console.WriteLine(
                $"  {row.Strategy,-14} {row.TreeEditDistance,9:0.000} {row.Sections,10} {row.MaxDepth,7} "
                + $"{row.InferredTitles,10} {row.CrossWindowSections,14} {(row.TreeSchemaPassed ? "  pass" : "  FAIL"),7}");
        }

        if (rows.Count == 2)
        {
            var delta = rows[1].TreeEditDistance - rows[0].TreeEditDistance;
            Console.WriteLine();
            Console.WriteLine(delta == 0
                ? $"  {rows[0].Strategy} and {rows[1].Strategy} are the same distance from gold."
                : $"  {rows[1].Strategy} is {Math.Abs(delta):0.000} {(delta < 0 ? "closer to" : "further from")} "
                    + $"gold than {rows[0].Strategy}.");
        }

        Console.WriteLine();
        Console.WriteLine("  cost and wall clock are in model_calls, keyed by run id; they are not in the artifact.");
    }

    private static async Task<GoldDocument> ReadGoldAsync(string path) =>
        JsonSerializer.Deserialize<GoldDocument>(
            await File.ReadAllTextAsync(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            })
        ?? throw new ArgumentException($"'{path}' did not parse as a gold document");

    /// <summary>
    /// Reads a worker-written <c>05_tree.json</c>. <c>segctl segment</c> writes the bare
    /// <c>SectionNode</c> under the same name, which parses as a <see cref="TreeArtifact"/> with a
    /// null root — so the shape is checked rather than left to fail as a null reference three
    /// frames later.
    /// </summary>
    private static async Task<TreeArtifact> ReadTreeAsync(string path)
    {
        var artifact = JsonSerializer.Deserialize<TreeArtifact>(
            await File.ReadAllTextAsync(path),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
            });

        return artifact?.Root is null
            ? throw new ArgumentException(
                $"'{path}' has no 'root' — this command reads the worker's 05_tree.json, not the bare "
                + "section tree that 'segctl segment' writes")
            : artifact;
    }
}
