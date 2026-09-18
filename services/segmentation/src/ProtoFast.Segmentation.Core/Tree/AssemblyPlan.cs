using System.Globalization;
using System.Text.Json.Serialization;

namespace ProtoFast.Segmentation.Core.Tree;

/// <summary>
/// How a window agent's node is named in every message the orchestration exchanges.
///
/// <para>References rather than content is the load-bearing decision of the orchestrator plan
/// (§4.3): the orchestrator never emits a paragraph id and never emits paragraph text, so the
/// <c>text-integrity</c> invariant is strengthened by adding an agent rather than weakened. A
/// reference is <c>W00003:n7</c> — window 3, the seventh node of its subtree in document order.
/// </para>
/// </summary>
public static class NodeRefs
{
    public static string For(int windowIndex, int nodeIndex) =>
        $"W{windowIndex.ToString("D5", CultureInfo.InvariantCulture)}:n{nodeIndex.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? value, out int windowIndex, out int nodeIndex)
    {
        windowIndex = 0;
        nodeIndex = 0;

        if (value is null || value.Length < 9 || value[0] != 'W')
        {
            return false;
        }

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || colon + 2 > value.Length - 1 || value[colon + 1] != 'n')
        {
            return false;
        }

        return int.TryParse(value.AsSpan(1, colon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out windowIndex)
            && int.TryParse(value.AsSpan(colon + 2), NumberStyles.None, CultureInfo.InvariantCulture, out nodeIndex);
    }
}

/// <summary>
/// What one window agent reported, flattened to the shape the orchestrator reasons over.
///
/// <para>Never the subtree itself — those stay in S3 and are addressed by reference. An outline
/// row is titles, depth and span; on an eight-window document that is a few hundred lines rather
/// than the whole skeleton, which is what keeps the orchestrator's input small enough for the
/// round cap to be a real bound (orchestrator plan §4.1).</para>
/// </summary>
public sealed record WindowOutline(
    int WindowIndex,
    string FirstEntryId,
    string LastEntryId,
    IReadOnlyList<OutlineNode> Nodes,
    IReadOnlyList<string> OpenQuestions);

/// <summary>One node of a window's subtree, as the orchestrator sees it.</summary>
public sealed record OutlineNode(
    string Ref,
    int Depth,
    string Title,
    bool TitleInferred,
    string? HeadingLineId,
    int ParagraphCount,
    int DescendantCount);

/// <summary>Everything the bench produced in one round.</summary>
public sealed record BenchDigest(IReadOnlyList<WindowOutline> Windows);

/// <summary>
/// The questions the orchestrator may put back to a window agent (orchestrator plan §4.2).
///
/// <para>A closed vocabulary, not free text. The distinction is §12.1's: a directive
/// <em>selects</em> among behaviours the deployed image already contains, so nothing the
/// orchestrator emits can change <c>PromptVersion</c> or invalidate a qualification row. It is
/// also what keeps the follow-up round auditable — "which question did it ask" has a finite set
/// of answers.</para>
/// </summary>
public static class FollowUpKinds
{
    /// <summary>Does this node continue a section that began before the window?</summary>
    public const string ContinuesPrevious = "continues_previous";

    /// <summary>Which heading line, if any, is this node's real title?</summary>
    public const string TitleSource = "title_source";

    /// <summary>Does this node end where the window ends, or run past it?</summary>
    public const string BoundaryCheck = "boundary_check";

    /// <summary>Is this node a peer of the related node, or a child of it?</summary>
    public const string DepthCheck = "depth_check";

    public static readonly IReadOnlyList<string> All =
        [ContinuesPrevious, TitleSource, BoundaryCheck, DepthCheck];

    public static bool IsKnown(string? kind) =>
        kind is not null && All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>One question, addressed to the window that owns <see cref="Node"/>.</summary>
public sealed record FollowUp
{
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("node")]
    public string Node { get; init; } = string.Empty;

    /// <summary>The other node a <see cref="FollowUpKinds.DepthCheck"/> compares against.</summary>
    [JsonPropertyName("relatedNode")]
    public string RelatedNode { get; init; } = string.Empty;
}

/// <summary>A window agent's answer to one follow-up.</summary>
public sealed record FollowUpAnswer
{
    [JsonPropertyName("node")]
    public string Node { get; init; } = string.Empty;

    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    /// <summary><c>yes</c>, <c>no</c> or <c>unsure</c>.</summary>
    [JsonPropertyName("verdict")]
    public string Verdict { get; init; } = "unsure";

    /// <summary>A heading line id for <c>title_source</c>, a node reference otherwise. May be empty.</summary>
    [JsonPropertyName("evidence")]
    public string Evidence { get; init; } = string.Empty;
}

/// <summary>
/// One row of the assembled outline — the whole of what the orchestrator emits.
///
/// <para>A flat, depth-numbered list rather than a nested structure, which is not a compromise for
/// the decoder but the reason all five operations of orchestrator plan §4.3 need no operation
/// field at all. <c>attach</c> is a row's depth relative to the one above it; <c>group</c> is a
/// row with a title and no sources; <c>retitle</c> is a row with both; <c>merge</c> is a row with
/// two or more sources; and <c>drop_wrapper</c> is simply never naming a window's synthetic root.
/// An op list would have to make each of those separately valid, and a plan that is half applied
/// is worse than one that is rejected.</para>
/// </summary>
public sealed record AssemblyRow
{
    /// <summary>1 is a top-level section. A row may be at most one deeper than the row above it.</summary>
    [JsonPropertyName("depth")]
    public int Depth { get; init; } = 1;

    /// <summary>
    /// Window nodes this row is made of, in document order. Each contributes its whole subtree;
    /// two or more is a merge across a window boundary.
    /// </summary>
    [JsonPropertyName("sources")]
    public IReadOnlyList<string> Sources { get; init; } = [];

    /// <summary>
    /// Overrides the sources' title, or names a parent the source has no heading for. Empty keeps
    /// the first source's title.
    /// </summary>
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;
}

/// <summary>What the orchestrator returns each round: a finished plan, or more questions.</summary>
public sealed record AssemblyPlan
{
    [JsonPropertyName("outline")]
    public IReadOnlyList<AssemblyRow> Outline { get; init; } = [];

    [JsonPropertyName("followUps")]
    public IReadOnlyList<FollowUp> FollowUps { get; init; } = [];

    [JsonPropertyName("gaps")]
    public IReadOnlyList<CapabilityGapProposal> Gaps { get; init; } = [];

    /// <summary>A reply with an outline is the answer; a reply without one is another question.</summary>
    public bool IsComplete => Outline.Count > 0;
}

/// <summary>
/// A fix the orchestrator had to make by hand, recorded as evidence of a missing capability
/// (orchestrator plan §12.3(d)).
///
/// <para>Gaps are written and never read back. That is the whole of what keeps this outside
/// §24.1's blast radius: a gap reaches a person and a report, never a prompt. <see cref="Evidence"/>
/// is node references rather than quoted document text for the same reason the field lengths are
/// capped at write time — the audience for model-authored text in a database is the one prompt
/// injection targets when it cannot reach a model.</para>
/// </summary>
public sealed record CapabilityGapProposal
{
    /// <summary>
    /// <c>deterministic</c> — code could have done this; <c>agent</c> — a role that does not exist
    /// should have; <c>prompt</c> — an existing role was asked the wrong question. The first is the
    /// one to want: every time a model does something code could have done, a latent deterministic
    /// function has announced itself.
    /// </summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = string.Empty;

    [JsonPropertyName("evidence")]
    public IReadOnlyList<string> Evidence { get; init; } = [];

    [JsonPropertyName("observation")]
    public string Observation { get; init; } = string.Empty;

    [JsonPropertyName("proposal")]
    public string Proposal { get; init; } = string.Empty;
}

/// <summary>The gap kinds the writer accepts; anything else is dropped rather than stored.</summary>
public static class CapabilityGapKinds
{
    public const string Deterministic = "deterministic";
    public const string Agent = "agent";
    public const string Prompt = "prompt";

    public static readonly IReadOnlyList<string> All = [Deterministic, Agent, Prompt];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);
}
