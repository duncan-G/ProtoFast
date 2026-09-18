using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>Everything the labeler needs about the document it is working on.</summary>
public sealed record LabelingContext(
    string RunId,
    string DocumentId,
    string Family,
    Sensitivity Sensitivity,
    DocumentStatistics Statistics,
    IReadOnlyList<string> Instincts,
    IReadOnlyList<string> RunningOutline,
    IReadOnlyList<LineLabelResult> PreviousLabels,
    string? PinnedModelKey);

/// <summary>
/// Labels one window (plan §10.1).
///
/// <para>The tier escalation is the part worth reading: after the repair rounds and the targeted
/// follow-up are exhausted, the span is retried at the next tier up rather than accepted at low
/// confidence. A small model that cannot label a region is not made right by asking it a fourth
/// time, and the alternative — committing a guess — is what produces the wrong paragraphs that
/// nothing downstream can detect.</para>
/// </summary>
public sealed class LabelerAgent(
    AgentRunner runner,
    IOptions<PipelineOptions> options,
    ILogger<LabelerAgent> logger)
{
    private readonly LabelingOptions _labeling = options.Value.Labeling;
    private readonly RepairOptions _repair = options.Value.Repair;

    public async Task<IReadOnlyList<LineLabelResult>> LabelAsync(
        LabelWindow window,
        LabelingContext context,
        CancellationToken ct = default)
    {
        var expectedIds = window.Lines.Select(l => l.LineId).ToList();
        var routing = Routing(context, ModelTier.Small, expectedIds);

        var prompt = LabelPrompts.Build(
            runner.Assets, window, context.Statistics, context.Family,
            context.Instincts, context.RunningOutline, context.PreviousLabels);

        var result = await runner.RunAsync<LabelWindowReply>(
            prompt, routing,
            reply => Validate(reply, expectedIds),
            _repair.MaxRoundsPerArtifact, buildRepairPrompt: null, ct);

        if (!result.Success)
        {
            return await EscalateAsync(window, context, expectedIds, prompt, result.Validation, ct);
        }

        var labels = result.Value!.Labels.Select(l => l.ToResult()).ToList();
        return await ResolveUncertaintiesAsync(window, context, labels, ct);
    }

    /// <summary>
    /// One tier up, once (plan §9.8). Beyond that the run is better served by a human than by
    /// more spend.
    /// </summary>
    private async Task<IReadOnlyList<LineLabelResult>> EscalateAsync(
        LabelWindow window,
        LabelingContext context,
        IReadOnlyList<string> expectedIds,
        string prompt,
        ValidationResult failure,
        CancellationToken ct)
    {
        if (!_repair.EscalateOnceBeforeHuman)
        {
            throw new PipelineFailureException(
                PipelinePhase.Label,
                $"Window {window.WindowIndex} could not be labelled: {failure.ErrorReport}");
        }

        logger.LogWarning(
            "Escalating window {Window} of run {RunId} to the mid tier after '{Check}'.",
            window.WindowIndex, context.RunId, failure.CheckId);

        var escalated = await runner.RunAsync<LabelWindowReply>(
            prompt + "\n\n## What failed previously\n" + failure.ErrorReport,
            Routing(context, ModelTier.Mid, expectedIds),
            reply => Validate(reply, expectedIds),
            maxRounds: 1, buildRepairPrompt: null, ct);

        if (!escalated.Success)
        {
            throw new PipelineFailureException(
                PipelinePhase.Label,
                $"Window {window.WindowIndex} could not be labelled even after escalation: " +
                escalated.Validation.ErrorReport);
        }

        return [.. escalated.Value!.Labels.Select(l => l.ToResult())];
    }

    /// <summary>
    /// Re-asks about the lines that are either low-confidence or contradicted by strong layout
    /// evidence (plan §10.1). Only those lines and their immediate context go back, which is what
    /// keeps the follow-up a fraction of the original call's cost.
    /// </summary>
    private async Task<IReadOnlyList<LineLabelResult>> ResolveUncertaintiesAsync(
        LabelWindow window,
        LabelingContext context,
        List<LineLabelResult> labels,
        CancellationToken ct)
    {
        var byId = window.Lines.ToDictionary(l => l.LineId, StringComparer.Ordinal);

        for (var round = 0; round < _labeling.MaxFollowUpRounds; round++)
        {
            var uncertain = FindUncertain(labels, byId);
            if (uncertain.Count == 0)
            {
                return labels;
            }

            var followUp = LabelPrompts.BuildFollowUp(runner.Assets, window, uncertain);
            var ids = uncertain.Select(u => u.Label.LineId).ToList();

            var result = await runner.RunAsync<LabelWindowReply>(
                followUp,
                Routing(context, ModelTier.Small, ids),
                reply => Checks.CheckIdCoverage(ids, [.. reply.Labels.Select(l => l.Id)], requireOrder: false),
                maxRounds: 1, buildRepairPrompt: null, ct);

            if (!result.Success)
            {
                // A failed follow-up leaves the original answers in place. They were low
                // confidence, not invalid, and validation will catch them if they are wrong.
                logger.LogInformation(
                    "Follow-up round {Round} for window {Window} did not resolve; keeping original labels.",
                    round + 1, window.WindowIndex);
                return labels;
            }

            var replacements = result.Value!.Labels.ToDictionary(l => l.Id, l => l.ToResult(), StringComparer.Ordinal);
            for (var i = 0; i < labels.Count; i++)
            {
                if (replacements.TryGetValue(labels[i].LineId, out var replacement))
                {
                    labels[i] = replacement;
                }
            }
        }

        return labels;
    }

    /// <summary>
    /// The conflict table of plan §10.1: a label is uncertain when the model said so, or when the
    /// layout says something the label contradicts.
    /// </summary>
    internal List<(LineLabelResult Label, string Evidence)> FindUncertain(
        IReadOnlyList<LineLabelResult> labels,
        IReadOnlyDictionary<string, LineRecord> byId)
    {
        var uncertain = new List<(LineLabelResult, string)>();

        for (var i = 0; i < labels.Count; i++)
        {
            var label = labels[i];

            if (label.Confidence < _labeling.UncertainConfidence)
            {
                uncertain.Add((label, $"confidence {label.Confidence:0.00} is below {_labeling.UncertainConfidence:0.00}"));
                continue;
            }

            if (!byId.TryGetValue(label.LineId, out var line) || line.Layout is not { } layout)
            {
                continue;
            }

            var previousWidth = i > 0 && byId.TryGetValue(labels[i - 1].LineId, out var previous)
                ? previous.Layout?.Width
                : null;

            if (label.Label == LineLabel.Cont && layout.GapAbove >= 2.0 && previousWidth is < 0.6)
            {
                uncertain.Add((label, $"g={layout.GapAbove:0.0} and the previous line was w={previousWidth:0.00} — that usually means a new paragraph"));
            }
            else if (label.Label == LineLabel.Cont && layout.FontScale >= 1.25 && line.Text.Length < 80)
            {
                uncertain.Add((label, $"f={layout.FontScale:0.00} on a short line — that usually means a heading"));
            }
            else if (label.Label == LineLabel.Head
                && layout.FontScale is > 0.95 and < 1.05
                && !layout.IsBold
                && line.Text.TrimEnd().EndsWith('.')
                && Core.Ingest.TextMetrics.WordCount(line.Text) > 12)
            {
                uncertain.Add((label, $"f={layout.FontScale:0.00}, not bold, ends with a period, {Core.Ingest.TextMetrics.WordCount(line.Text)} words — that is usually body text"));
            }
        }

        return uncertain;
    }

    private static ValidationResult Validate(LabelWindowReply reply, IReadOnlyList<string> expectedIds)
    {
        var coverage = Checks.CheckIdCoverage(expectedIds, [.. reply.Labels.Select(l => l.Id)]);
        if (!coverage.Passed)
        {
            return coverage;
        }

        // Heading levels are assigned by the separate phase-3b pass, so they are not required here.
        return Checks.CheckLabelEnum([.. reply.Labels.Select(l => l.ToResult())], requireHeadingLevels: false);
    }

    private RoutingContext Routing(LabelingContext context, ModelTier tier, IReadOnlyList<string> ids) =>
        new(
            context.RunId, context.DocumentId, PipelinePhase.Label, AgentRole.Labeler, tier,
            context.Sensitivity,
            EstimatedInputTokens: 1_500 + ids.Count * 14,
            // Roughly eight tokens per label plus JSON overhead (plan §29.1).
            MaxOutputTokens: Math.Max(512, ids.Count * 12))
        {
            PinnedModelKey = context.PinnedModelKey,
            PromptVersion = runner.Assets.VersionFor(AgentRole.Labeler),
            Unit = $"window:{ids.Count}",
            OutputSchema = runner.Assets.WireSchemaElement("labels"),
            OutputSchemaName = "labels",
        };
}

/// <summary>A phase could not complete. Carries the phase so the run's error names where it stopped.</summary>
public sealed class PipelineFailureException(PipelinePhase phase, string message, bool permanent = false)
    : Exception(message)
{
    public PipelinePhase Phase { get; } = phase;

    /// <summary>
    /// True when a retry cannot help — an unconvertible document, a format the converter refuses.
    /// The consumer deletes the message instead of leaving it for redelivery, so one malformed
    /// file does not spend five delivery attempts on its way to the DLQ (ingest plan §9).
    /// </summary>
    public bool Permanent { get; } = permanent;
}
