using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Pipeline;

/// <summary>
/// Runs or resumes one document's workflow.
///
/// <para>Every method here is idempotent, because the SQS consumer cannot know whether a message
/// is the first delivery or the fifth. That guarantee comes from below — every phase checks its
/// artifact's idempotency key before doing work — rather than from a lock here, which is what
/// lets two workers briefly overlap after a visibility expiry without corrupting anything.</para>
/// </summary>
public interface IWorkflowHost
{
    /// <summary>
    /// Starts the run, or resumes it from its last checkpoint. Returns when the run reaches a
    /// terminal state <em>or</em> suspends on the human gate — both mean the message can be
    /// deleted, because a gated run is re-queued by <c>SubmitReviewDecision</c>.
    /// </summary>
    Task<RunOutcome> RunOrResumeAsync(string runId, PipelinePhase? fromPhase = null, CancellationToken ct = default);

    /// <summary>
    /// Posts a human decision back into a suspended workflow and lets it continue (plan §9.10).
    /// </summary>
    Task<RunOutcome> ResumeWithDecisionAsync(
        string runId, Executors.ReviewDecision decision, CancellationToken ct = default);
}

public enum RunOutcome
{
    /// <summary>Published. The message can be deleted.</summary>
    Completed,

    /// <summary>Suspended on the human gate. The message can be deleted; a decision re-queues it.</summary>
    AwaitingReview,

    /// <summary>The owner cancelled it. The message can be deleted.</summary>
    Cancelled,

    /// <summary>Failed in a way a retry might fix. The message returns to the queue.</summary>
    Failed,

    /// <summary>
    /// Failed in a way no retry can fix — an unreadable document, a format the converter refuses.
    /// The message is deleted: redelivering it would spend four more attempts reaching the same
    /// answer, and the run already carries the reason a person needs (ingest plan §23).
    /// </summary>
    FailedPermanently,
}
