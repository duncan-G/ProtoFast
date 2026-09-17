using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;

namespace ProtoFast.Segmentation.Storage;

/// <summary>
/// MAF's checkpoint store, backed by S3 under <c>runs/{runId}/_checkpoints/</c> (plan §13.2).
///
/// <para>This is what makes a mid-run deploy safe. The worker holds no run state of its own: when
/// its container stops, the in-flight SQS message's visibility expires, another worker receives
/// it, and the workflow resumes from the checkpoint written here. MAF ships a filesystem store,
/// which would tie a run to the box that started it — on a two-host setup where Host B can be
/// replaced, that is the same as losing the run.</para>
///
/// <para>The index is a separate small object rather than a listing, because <c>ListObjectsV2</c>
/// is eventually consistent for a prefix that was just written and "resume from the latest
/// checkpoint" cannot tolerate a stale answer.</para>
/// </summary>
public sealed class S3CheckpointStore(IArtifactStore artifacts) : ICheckpointStore<JsonElement>
{
    /// <summary>One checkpoint's place in the run's tree of checkpoints.</summary>
    private sealed record IndexEntry(string CheckpointId, string? ParentCheckpointId, DateTimeOffset At);

    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId, JsonElement value, CheckpointInfo? parent = null)
    {
        var checkpointId = $"{DateTimeOffset.UtcNow:yyyyMMddTHHmmssfff}-{Guid.NewGuid():N}"[..40];
        var key = ArtifactKeys.Checkpoint(sessionId, checkpointId);

        // The checkpoint lands before the index does. The reverse order would let a crash between
        // the two writes leave the index pointing at an object that does not exist, which resume
        // has no way to recover from — whereas an orphaned checkpoint is merely unused.
        await artifacts.WriteAsync(key, value, checkpointId);

        var index = await LoadIndexAsync(sessionId);
        index.Add(new IndexEntry(checkpointId, parent?.CheckpointId, DateTimeOffset.UtcNow));
        await artifacts.WriteAsync(ArtifactKeys.CheckpointIndex(sessionId), index, checkpointId);

        return new CheckpointInfo(sessionId, checkpointId);
    }

    public async ValueTask<JsonElement> RetrieveCheckpointAsync(string sessionId, CheckpointInfo key)
    {
        var value = await artifacts.ReadAsync<JsonElement>(ArtifactKeys.Checkpoint(sessionId, key.CheckpointId));
        if (value.ValueKind == JsonValueKind.Undefined)
        {
            throw new InvalidOperationException(
                $"Checkpoint '{key.CheckpointId}' for run '{sessionId}' is missing from S3. " +
                "Re-run the phase with RerunFrom rather than resuming.");
        }

        return value;
    }

    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId, CheckpointInfo? withParent = null)
    {
        var index = await LoadIndexAsync(sessionId);

        return
        [
            .. index
                .Where(e => withParent is null || e.ParentCheckpointId == withParent.CheckpointId)
                .OrderBy(e => e.At)
                .Select(e => new CheckpointInfo(sessionId, e.CheckpointId)),
        ];
    }

    private async Task<List<IndexEntry>> LoadIndexAsync(string sessionId) =>
        await artifacts.ReadAsync<List<IndexEntry>>(ArtifactKeys.CheckpointIndex(sessionId)) ?? [];
}
