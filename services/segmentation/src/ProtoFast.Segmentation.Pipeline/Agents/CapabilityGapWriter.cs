using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Tree;
using ProtoFast.Segmentation.Data;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>
/// Persists the orchestrator's capability gaps (orchestrator plan §12.3(d)).
///
/// <para>Every field is either from a closed vocabulary or truncated. The point is not defence in
/// depth for its own sake: these rows are model-authored text that is rendered to people, and a
/// gap report is exactly what prompt injection aims at when it cannot reach a prompt. A gap whose
/// evidence is not a node reference is not evidence — it is document text that found a way into a
/// database — so it is dropped rather than stored.</para>
///
/// <para>Writing is best-effort. A gap is a hypothesis for a person to read; failing a document
/// because the hypothesis could not be filed would be the tail wagging the dog.</para>
/// </summary>
public sealed class CapabilityGapWriter(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<CapabilityGapWriter> logger)
{
    public const int MaxTextLength = 500;
    public const int MaxEvidenceItems = 12;

    /// <summary>Gaps per run, so one long document cannot fill the table on its own.</summary>
    public const int MaxGapsPerRun = 20;

    public async Task WriteAsync(
        string runId,
        string documentFamily,
        IReadOnlyList<CapabilityGapProposal> gaps,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(gaps);

        var rows = gaps.Select(g => Sanitize(runId, documentFamily, g)).Where(r => r is not null).Take(MaxGapsPerRun).ToList();
        if (rows.Count == 0)
        {
            return;
        }

        try
        {
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();
            db.CapabilityGaps.AddRange(rows!);
            await db.SaveChangesAsync(ct);

            logger.LogInformation("Run {RunId}: recorded {Count} capability gaps.", runId, rows.Count);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Run {RunId}: capability gaps could not be recorded.", runId);
        }
    }

    private Data.Entities.CapabilityGap? Sanitize(
        string runId, string documentFamily, CapabilityGapProposal gap)
    {
        if (!CapabilityGapKinds.IsKnown(gap.Kind))
        {
            logger.LogDebug("Run {RunId}: dropped a gap of unknown kind '{Kind}'.", runId, gap.Kind);
            return null;
        }

        // Node references only. The evidence field is the one a crafted document would try to
        // reach, and "W00003:n7" is a shape nothing in a document can talk its way into.
        var evidence = gap.Evidence
            .Where(e => NodeRefs.TryParse(e, out _, out _))
            .Distinct(StringComparer.Ordinal)
            .Take(MaxEvidenceItems)
            .ToList();

        if (evidence.Count == 0
            || string.IsNullOrWhiteSpace(gap.Observation)
            || string.IsNullOrWhiteSpace(gap.Proposal))
        {
            return null;
        }

        return new Data.Entities.CapabilityGap
        {
            Kind = gap.Kind,
            Role = nameof(AgentRole.StructureOrchestrator),
            Observation = Truncate(gap.Observation),
            Proposal = Truncate(gap.Proposal),
            RunId = runId,
            Evidence = string.Join(',', evidence),
            DocumentFamily = documentFamily,
            CreatedAt = clock.GetUtcNow(),
        };
    }

    private static string Truncate(string value)
    {
        var trimmed = value.Trim();
        return trimmed.Length <= MaxTextLength ? trimmed : trimmed[..MaxTextLength];
    }
}
