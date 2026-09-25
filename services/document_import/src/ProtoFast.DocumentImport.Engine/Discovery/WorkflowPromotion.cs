namespace ProtoFast.DocumentImport.Engine;

/// <summary>
/// The human gate for a mined workflow. Promoting it seeds the stage policy rows and puts the
/// bucket into shadow: discovery stays primary, and a sample of runs also run the workflow until
/// its terminal pass rate flips the bucket to scheduled.
///
/// <para>This writes outside the updater's partition. It is a rare, human-driven call, so the
/// race with a concurrent outcome for the same bucket is accepted rather than routed through the
/// bus.</para>
/// </summary>
public sealed class WorkflowPromotion(
    IRegistry registry,
    IMinedWorkflowStore drafts,
    IPolicyStore policies,
    IBucketPolicyStore buckets,
    TimeProvider time)
{
    public async Task PromoteAsync(WorkflowRef workflow, CancellationToken ct)
    {
        var (bucket, mined) = await drafts.GetAsync(workflow, ct)
            ?? throw new KeyNotFoundException($"No mined draft for workflow {workflow.Id}@{workflow.Version}.");

        await registry.PromoteAsync(workflow, ct);

        var now = time.GetUtcNow();
        foreach (var seed in mined.Seeds)
        {
            // A row that has learned more than the seed knows (from an earlier workflow of this
            // bucket) keeps what it learned.
            var current = await policies.GetAsync(bucket, seed.StageId, ct);
            if (seed.Confidence.Observations > current.Confidence.Observations)
            {
                await policies.PutAsync(seed with { Bucket = bucket, UpdatedAt = now }, ct);
            }
        }

        var policy = await buckets.GetAsync(bucket, ct);
        await buckets.PutAsync(
            policy with { Mode = RunMode.Discovery, Workflow = workflow, Confidence = Confidence.Prior, UpdatedAt = now },
            ct);
    }
}
