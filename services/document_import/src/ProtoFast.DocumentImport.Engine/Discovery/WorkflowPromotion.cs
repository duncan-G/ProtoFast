using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Discovery;

/// <summary>Writes outside the updater's partition; accepted because promotion is rare and manual.</summary>
public sealed class WorkflowPromotion(
    IRegistry registry,
    IMinedWorkflowStore drafts,
    IPolicyStore policies,
    IDocumentFamilyPolicyStore families,
    TimeProvider time)
{
    public async Task PromoteAsync(WorkflowRef workflow, CancellationToken ct)
    {
        var (family, mined) = await drafts.GetAsync(workflow, ct)
            ?? throw new KeyNotFoundException($"No mined draft for workflow {workflow.Id}@{workflow.Version}.");

        await registry.PromoteAsync(workflow, ct);

        var now = time.GetUtcNow();
        foreach (var seed in mined.Seeds)
        {
            // Keep a row that has learned more than the seed.
            var current = await policies.GetAsync(family, seed.StageId, ct);
            if (seed.Confidence.Observations > current.Confidence.Observations)
            {
                await policies.PutAsync(seed with { Family = family, UpdatedAt = now }, ct);
            }
        }

        var policy = await families.GetAsync(family, ct);
        await families.PutAsync(
            policy with { Mode = RunMode.Discovery, Workflow = workflow, Confidence = Confidence.Prior, UpdatedAt = now },
            ct);
    }
}
