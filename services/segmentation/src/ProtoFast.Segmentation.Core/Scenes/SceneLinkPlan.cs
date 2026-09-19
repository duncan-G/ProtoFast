using System.Text.Json.Serialization;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Scenes;

/// <summary>One window of consecutive scene digests, with overlap so a near frame stays in window.</summary>
public sealed record SceneLinkWindow(
    int WindowIndex,
    IReadOnlyList<SceneDigest> Digests,
    int CommitStart,
    int CommitEndExclusive)
{
    public bool Commits(int ordinal) => ordinal >= CommitStart && ordinal < CommitEndExclusive;
}

/// <summary>What one link windower reported.</summary>
public sealed record SceneLinkWindowResult(
    int WindowIndex,
    IReadOnlyList<LinkProposal> Links,
    IReadOnlyList<string> OpenQuestions,
    string? ModelKey);

/// <summary>Everything the link bench produced in one round — digests of proposals, never text.</summary>
public sealed record SceneLinkDigest(IReadOnlyList<SceneLinkWindowResult> Windows)
{
    public IEnumerable<LinkProposal> AllLinks => Windows.SelectMany(w => w.Links);
}

/// <summary>
/// One proposed link. The orchestrator composes <b>references</b> — scene ids code issued and link
/// kinds from a closed enum — and emits no title, no span and no text, so the same guarantee holds
/// as in phases 6 and 9: adding an agent cannot touch <c>text-integrity</c>.
/// </summary>
public sealed record LinkProposal
{
    [JsonPropertyName("from")]
    public string From { get; init; } = string.Empty;

    [JsonPropertyName("to")]
    public string To { get; init; } = string.Empty;

    /// <summary>One of <see cref="SceneLinkKind"/>, lowercased.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary>
    /// The ids justifying the link (C13). Never empty: an uncited link is not materialized, because
    /// K4 is worth less than C11.
    /// </summary>
    [JsonPropertyName("evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = [];

    [JsonPropertyName("confidence")]
    public double Confidence { get; init; } = 0.5;
}

/// <summary>What the link orchestrator returns each round: a finished link plan, or more questions.</summary>
public sealed record SceneLinkPlan
{
    [JsonPropertyName("links")]
    public IReadOnlyList<LinkProposal> Links { get; init; } = [];

    [JsonPropertyName("followUps")]
    public IReadOnlyList<Tree.FollowUp> FollowUps { get; init; } = [];

    [JsonPropertyName("gaps")]
    public IReadOnlyList<Tree.CapabilityGapProposal> Gaps { get; init; } = [];

    /// <summary>
    /// A plan is complete when it has been emitted at all — <b>including an empty one</b>. Unlike
    /// an assembly plan, "no links" is a real and common answer: most textbooks and most
    /// transcripts have no frames and no flashbacks (§8.9).
    /// </summary>
    [JsonPropertyName("complete")]
    public bool Complete { get; init; } = true;

    public bool IsComplete => Complete || Links.Count > 0;
}

/// <summary>The questions a link orchestrator may put back to a windower.</summary>
public static class SceneLinkFollowUpKinds
{
    /// <summary>Does this scene resume something that began before your window?</summary>
    public const string ResumesEarlier = "resumes_earlier";

    /// <summary>Is this scene's time relation to the one before it backwards?</summary>
    public const string TimeDirection = "time_direction";

    /// <summary>Does this scene frame the one after it — a story being told inside it?</summary>
    public const string FrameCheck = "frame_check";

    public static readonly IReadOnlyList<string> All = [ResumesEarlier, TimeDirection, FrameCheck];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);
}
