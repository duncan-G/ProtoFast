using System.Text.Json;
using System.Text.Json.Serialization;
using ProtoFast.Segmentation.Core.Evaluation;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;

namespace ProtoFast.Segmentation.UnitTests;

/// <summary>
/// The CI evaluation gate (plan §26.6), run against the committed <c>gold-dev</c> subset.
///
/// <para>What this asserts is a <em>baseline</em>, and the plan is explicit that the baseline must
/// be able to fail: "a trivial baseline should score below threshold on the degraded subset,
/// proving the checks can fail" (§6). So the degraded document is asserted to MISS its boundary
/// thresholds on the deterministic path — because without layout metadata there is nothing for the
/// cleaning rules to find, and recovering that document is precisely the labelling model's job.
/// If a future change makes it pass here, that is a real improvement and this test should be
/// updated to say so; if a change makes clean or partial fail, that is a regression and this test
/// blocks the merge.</para>
/// </summary>
public class EvaluationGateTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
    };

    public static TheoryData<string> GoldDocuments()
    {
        var data = new TheoryData<string>();
        foreach (var file in Directory.GetFiles(GoldDirectory, "*.json").Order())
        {
            data.Add(Path.GetFileNameWithoutExtension(file));
        }

        return data;
    }

    [Fact]
    public void TheGoldSubsetIsPresentAndCoversEveryConditionBucket()
    {
        var documents = LoadAll();

        Assert.NotEmpty(documents);
        Assert.Equal(
            Enum.GetValues<ConditionBucket>().ToHashSet(),
            documents.Select(d => d.Condition).ToHashSet());
    }

    [Theory]
    [MemberData(nameof(GoldDocuments))]
    public void TextIntegrityHoldsForEveryGoldDocument(string documentId)
    {
        // The hard gate (plan N1). It has no condition bucket and no tolerance: a document whose
        // text the pipeline could not reproduce is a defect whatever its condition.
        var gold = Load(documentId);
        var run = DeterministicPipeline.Run(gold.Markdown, gold.Layout);

        var integrity = run.Validation.Results.Single(r => r.CheckId == Checks.TextIntegrity);
        Assert.True(integrity.Passed, integrity.ErrorReport);
    }

    [Theory]
    [MemberData(nameof(GoldDocuments))]
    public void EveryGoldDocumentProducesAStructurallyValidTree(string documentId)
    {
        var gold = Load(documentId);
        var run = DeterministicPipeline.Run(gold.Markdown, gold.Layout);

        foreach (var checkId in new[] { Checks.TreeShape, Checks.Contiguity, Checks.HeadingAnchor })
        {
            var result = run.Validation.Results.Single(r => r.CheckId == checkId);
            Assert.True(result.Passed, result.ErrorReport);
        }
    }

    [Theory]
    [InlineData("greenhouse-trial")]
    [InlineData("archive-survey")]
    [InlineData("site-interview")]
    public void DocumentsTheDeterministicPathCanHandleMeetTheirThresholds(string documentId)
    {
        var (gold, metrics) = Evaluate(documentId);
        var thresholds = AcceptanceThresholds.For(gold.Condition);

        Assert.True(
            metrics.Passes(thresholds),
            $"{documentId} ({gold.Condition}) missed its thresholds: " +
            $"Pk {metrics.Pk:0.000} (≤ {thresholds.MaxPk}), " +
            $"WindowDiff {metrics.WindowDiff:0.000} (≤ {thresholds.MaxWindowDiff}), " +
            $"heading F1 {metrics.HeadingF1.F1:0.000} (≥ {thresholds.MinHeadingF1}), " +
            $"tree distance {metrics.TreeEditDistance:0.000} (≤ {thresholds.MaxTreeEditDistance}), " +
            $"artifact recall {metrics.ArtifactRemoval.Recall:0.000} (≥ {thresholds.MinArtifactRecall})");
    }

    [Fact]
    public void TheDegradedDocumentMissesItsThresholdsWithoutAModel()
    {
        // This is the plan's own requirement that the baseline must be able to fail (§6). The
        // bridge survey has running heads and page numbers but no layout metadata, so the cleaning
        // rules that would strip them cannot fire and there are no boundaries to trust — which is
        // exactly the case the labelling model exists for.
        var (gold, metrics) = Evaluate("bridge-survey");

        Assert.Equal(ConditionBucket.Degraded, gold.Condition);
        Assert.False(
            metrics.Passes(AcceptanceThresholds.For(gold.Condition)),
            "the degraded document now passes on the deterministic path alone. That is an "
            + "improvement, not a failure — update this test to assert the new behaviour.");

        // But text integrity still holds, because that is not a quality metric.
        Assert.True(metrics.TextIntegrityPassed);
    }

    [Fact]
    public void PassAtKIsStableBecauseTheDeterministicPathIsDeterministic()
    {
        // pass^k asks whether a document passes on EVERY one of k runs (Appendix D). With no model
        // in the loop the answer must be identical each time — if it is not, something in the
        // pipeline depends on iteration order or on a clock, and that would make every other
        // measurement here noise.
        var runs = Enumerable.Range(0, 3)
            .Select(_ => LoadAll().Select(g => Evaluate(g.DocumentId).Metrics).ToList())
            .ToList();

        var perDocument = Enumerable.Range(0, runs[0].Count)
            .Select(i => (IReadOnlyList<DocumentMetrics>)runs.Select(r => r[i]).ToList())
            .ToList();

        Assert.All(perDocument, metrics => Assert.All(metrics, m => Assert.Equal(metrics[0].Pk, m.Pk, 10)));

        // Three of the four documents pass, so pass^3 is 0.75 — and it is exactly 0.75 every time.
        Assert.Equal(0.75, GoldEvaluator.PassAtK(perDocument), 10);
    }

    private static (GoldDocument Gold, DocumentMetrics Metrics) Evaluate(string documentId)
    {
        var gold = Load(documentId);
        var run = DeterministicPipeline.Run(gold.Markdown, gold.Layout);

        var metrics = GoldEvaluator.Evaluate(gold, new EvaluatedRun(
            run.Assembly.Paragraphs,
            run.Assembly.Headings,
            run.Root,
            run.Cleaning.Lines
                .Where(line => run.Cleaning.ArtifactLineIds.Contains(line.LineId))
                .Select(line => line.Text)
                .ToHashSet(StringComparer.Ordinal),
            run.Validation,
            CostUsd: 0,
            Duration: TimeSpan.Zero));

        return (gold, metrics);
    }

    private static IReadOnlyList<GoldDocument> LoadAll() =>
    [
        .. Directory.GetFiles(GoldDirectory, "*.json").Order()
            .Select(f => Path.GetFileNameWithoutExtension(f))
            .Select(Load),
    ];

    private static GoldDocument Load(string documentId) =>
        JsonSerializer.Deserialize<GoldDocument>(
            File.ReadAllText(Path.Combine(GoldDirectory, $"{documentId}.json")), Json)
        ?? throw new InvalidOperationException($"'{documentId}' is not a valid gold document.");

    private static string GoldDirectory => Path.Combine(AppContext.BaseDirectory, "gold-dev");
}
