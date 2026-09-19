using System.Globalization;
using System.Text.Json.Serialization;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Core.Personas;

/// <summary>
/// How a window agent's referent candidate is named in every message phase 9 exchanges.
///
/// <para>The same discipline as <c>NodeRefs</c>, for the same reason (orchestrator plan §4.3): the
/// orchestrator composes <em>references</em>, never content. It never emits a surface form, a
/// paragraph id or a tag id it was not given — it names candidates the windowers produced. So the
/// only ids in play are ones code issued, and adding an agent here strengthens
/// <c>text-integrity</c> rather than weakening it.</para>
/// </summary>
public static class CandidateRefs
{
    public static string For(int windowIndex, int candidateIndex) =>
        $"W{windowIndex.ToString("D5", CultureInfo.InvariantCulture)}:c{candidateIndex.ToString(CultureInfo.InvariantCulture)}";

    public static bool TryParse(string? value, out int windowIndex, out int candidateIndex)
    {
        windowIndex = 0;
        candidateIndex = 0;

        if (value is null || value.Length < 9 || value[0] != 'W')
        {
            return false;
        }

        var colon = value.IndexOf(':', StringComparison.Ordinal);
        if (colon < 0 || colon + 2 > value.Length - 1 || value[colon + 1] != 'c')
        {
            return false;
        }

        return int.TryParse(value.AsSpan(1, colon - 1), NumberStyles.None, CultureInfo.InvariantCulture, out windowIndex)
            && int.TryParse(value.AsSpan(colon + 2), NumberStyles.None, CultureInfo.InvariantCulture, out candidateIndex);
    }
}

/// <summary>
/// One local cluster a windower is confident co-refers <em>inside its own window</em> — "she" =
/// "the professor" = this candidate.
///
/// <para>The orchestrator sees these as a digest and nothing else. It is enough: whether window 3's
/// "the professor" is window 1's "Dr. Vance" is a question about surface forms and first
/// appearances, not about the paragraphs they appear in.</para>
/// </summary>
public sealed record ReferentCandidate(
    string Ref,
    TagKind Kind,
    string SurfaceForm,
    IReadOnlyList<string> SurfaceForms,
    IReadOnlyList<string> TagIds,

    /// <summary>The leaf section the candidate's first tag sits in; what a scope is resolved from.</summary>
    string? SectionId,

    /// <summary>Set by the windower for a group it resolved from a conjunction of individuals (§4.3).</summary>
    GroupFormation? Formation,

    IReadOnlyList<string> MemberRefs,
    bool MembershipComplete,
    double Confidence);

/// <summary>What one windower reported, flattened to what the orchestrator reasons over.</summary>
public sealed record CandidateWindow(
    int WindowIndex,
    string FirstParagraphId,
    string LastParagraphId,
    IReadOnlyList<ReferentCandidate> Candidates,
    IReadOnlyList<string> OpenQuestions);

/// <summary>Everything the persona bench produced in one round.</summary>
public sealed record CandidateDigest(IReadOnlyList<CandidateWindow> Windows)
{
    public IEnumerable<ReferentCandidate> AllCandidates => Windows.SelectMany(w => w.Candidates);
}

/// <summary>
/// The questions the persona orchestrator may put back to a windower. A closed vocabulary for the
/// same reason phase 6's is: a directive <em>selects</em> among behaviours the deployed image
/// already contains, so nothing the orchestrator emits can change <c>PromptVersion</c>.
/// </summary>
public static class PersonaFollowUpKinds
{
    /// <summary>Is this candidate introduced before the window, by a pronoun or a bare reference?</summary>
    public const string IntroducedEarlier = "introduced_earlier";

    /// <summary>Which surface form is this candidate's own name, as the text writes it?</summary>
    public const string CanonicalForm = "canonical_form";

    /// <summary>Is this candidate a group, and is its membership enumerated in your window?</summary>
    public const string GroupCheck = "group_check";

    /// <summary>Does this candidate appear only in this scene, or throughout?</summary>
    public const string ScopeCheck = "scope_check";

    public static readonly IReadOnlyList<string> All =
        [IntroducedEarlier, CanonicalForm, GroupCheck, ScopeCheck];

    public static bool IsKnown(string? kind) => kind is not null && All.Contains(kind, StringComparer.Ordinal);
}

/// <summary>One row of the registry plan — one registry entry, made of one or more candidates.</summary>
public sealed record RegistryRow
{
    /// <summary>Candidates merged into this entry, in document order. Two or more is a coreference claim.</summary>
    [JsonPropertyName("candidates")]
    public IReadOnlyList<string> Candidates { get; init; } = [];

    /// <summary>
    /// The entry's name. Generated metadata under C9 — clearly marked, never re-entering the
    /// content stream — but in practice always one of the candidates' own surface forms, because
    /// that is what the prompt asks for.
    /// </summary>
    [JsonPropertyName("canonicalName")]
    public string CanonicalName { get; init; } = string.Empty;

    /// <summary><c>individual</c> or <c>group</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; init; } = "individual";

    /// <summary><c>persistent</c> or <c>scene-local</c>.</summary>
    [JsonPropertyName("scope")]
    public string Scope { get; init; } = "persistent";

    /// <summary><c>enumerated</c> or <c>named</c>; read only for a group.</summary>
    [JsonPropertyName("formation")]
    public string Formation { get; init; } = string.Empty;
}

/// <summary>What the persona orchestrator returns each round: a finished registry, or more questions.</summary>
public sealed record RegistryPlan
{
    [JsonPropertyName("registry")]
    public IReadOnlyList<RegistryRow> Registry { get; init; } = [];

    [JsonPropertyName("followUps")]
    public IReadOnlyList<Tree.FollowUp> FollowUps { get; init; } = [];

    [JsonPropertyName("gaps")]
    public IReadOnlyList<Tree.CapabilityGapProposal> Gaps { get; init; } = [];

    public bool IsComplete => Registry.Count > 0;
}
