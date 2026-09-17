using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline.Executors;

/// <summary>
/// "Has this phase already been done, with this exact input?" — asked once, in one place.
///
/// <para>This is the mechanism behind plan N4. A re-delivered SQS message, a resumed workflow, or
/// a deploy that recreated the worker mid-run all replay phases that already finished; each one
/// asks here first and returns its existing artifact instead of doing the work again. Because the
/// answer comes from the object's own metadata, it is true about the artifact that exists rather
/// than about a database row claiming one does.</para>
/// </summary>
public sealed class PhaseGate(IArtifactStore store)
{
    /// <summary>
    /// The artifact this phase already produced for <paramref name="idempotencyKey"/>, or null if
    /// there is work to do. A <paramref name="rerunFrom"/> at or before this phase always returns
    /// null — that is what <c>RerunFrom</c> means.
    /// </summary>
    public async Task<ArtifactRef?> AlreadyDoneAsync(
        string runId,
        PipelinePhase phase,
        string idempotencyKey,
        PipelinePhase? rerunFrom,
        CancellationToken ct)
    {
        if (rerunFrom is not null && rerunFrom <= phase)
        {
            return null;
        }

        return await store.FindExistingAsync(ArtifactKeys.Phase(runId, phase), idempotencyKey, ct);
    }

    /// <summary>The same question for a fan-out unit that has its own key (a window, a paragraph).</summary>
    public Task<ArtifactRef?> AlreadyDoneAsync(string key, string idempotencyKey, CancellationToken ct) =>
        store.FindExistingAsync(key, idempotencyKey, ct);
}
