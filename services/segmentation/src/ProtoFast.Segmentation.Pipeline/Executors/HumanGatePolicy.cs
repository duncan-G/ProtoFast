using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Data;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// Decides whether a run stops for a person (plan §9.10).
///
/// <para>The policy is deliberately generous at the start of a family's life and then gets out of
/// the way: after K documents of a family have been approved, the gate retires for that family.
/// That is what keeps human review from becoming the throughput ceiling (plan §31) while still
/// catching the case it exists for — a document shape nobody has checked the pipeline against.</para>
/// </summary>
public sealed class HumanGatePolicy(IServiceScopeFactory scopes, IOptions<PipelineOptions> options)
{
    private readonly HumanGateOptions _gate = options.Value.HumanGate;

    public async Task<bool> RequiresHumanAsync(
        Data.Entities.Run run,
        bool validationPassed,
        int unresolvedHighFindings,
        CancellationToken ct = default)
    {
        // The owner asked for it explicitly.
        if (run.RequiresReview)
        {
            return true;
        }

        // Validation could not be repaired, or the reviewer found something serious.
        if (!validationPassed || unresolvedHighFindings > 0)
        {
            return true;
        }

        if (_gate.RequireForRestricted && run.Sensitivity == Sensitivity.Restricted)
        {
            return true;
        }

        return await ApprovedInFamilyAsync(run.DocumentFamily, ct) < _gate.MinApprovedPerFamily;
    }

    /// <summary>
    /// How many documents of this family a person has approved. Counted from
    /// <c>review_tasks</c> rather than from a column on the family, because the review decisions
    /// <em>are</em> the record — a counter would be a second source of truth to keep honest.
    /// </summary>
    internal async Task<int> ApprovedInFamilyAsync(string family, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<SegmentationDbContext>();

        return await db.ReviewTasks
            .AsNoTracking()
            .CountAsync(
                r => r.DocumentFamily == family
                    && r.Status == "complete"
                    && (r.Decision == ReviewDecisionKind.Approve || r.Decision == ReviewDecisionKind.ApproveWithEdits),
                ct);
    }
}
