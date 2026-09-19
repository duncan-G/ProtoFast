namespace ProtoFast.Segmentation.Core.Model;

// The records of the scene plan §10 and of [unit §8]. Three layers in one file because they are
// defined against each other: a scene names items, an item names a span of a frozen paragraph and
// carries tags, and a tag names a registry id. None of them carries text (C9).

// ---- Presentation (phase 5) -------------------------------------------------------------------

/// <summary>
/// One paragraph's presentation verdict (scene plan §5). <see cref="Class"/> is non-null exactly
/// when the paragraph is metadata; the pair is checked rather than modelled as a discriminated
/// union because a model's answer has to be representable before it can be rejected with a useful
/// error — the same reasoning as <see cref="SectionNode"/>'s children-xor-paragraphs.
/// </summary>
public sealed record ParagraphPresentation(
    string ParagraphId,
    Presentation Presentation,
    MetadataClass? Class,
    IReadOnlyList<string> EvidenceIds,
    double Confidence)
{
    public bool IsDisplayable => Presentation == Presentation.Displayable;

    public static ParagraphPresentation Displayable(string paragraphId, double confidence = 1.0) =>
        new(paragraphId, Model.Presentation.Displayable, null, [], confidence);

    public static ParagraphPresentation Metadata(
        string paragraphId, MetadataClass @class, IReadOnlyList<string> evidenceIds, double confidence) =>
        new(paragraphId, Model.Presentation.Metadata, @class, evidenceIds, confidence);
}

/// <summary>
/// The per-paragraph composition-family evidence phase 5 emits as a by-product (scene plan §6.1).
/// Phase 7 aggregates it onto the tree in code to derive family scopes; this is the only thing
/// standing between "a section is a different kind of work" and a second detector.
/// </summary>
/// <param name="DialogueRatio">Share of the paragraph's characters inside quotation marks.</param>
/// <param name="HasSpeakerLabel">A leading <c>NAME:</c> turn marker.</param>
/// <param name="ImperativeDensity">Share of sentences opening with a bare verb.</param>
/// <param name="SecondPerson">"you", "your" — instruction and direct address.</param>
/// <param name="PastTense">Share of finite verbs in the past tense, as a cheap tense proxy.</param>
public sealed record FamilyEvidence(
    string ParagraphId,
    double DialogueRatio,
    bool HasSpeakerLabel,
    double ImperativeDensity,
    double SecondPerson,
    double PastTense,
    int WordCount);

// ---- Items (phase 8) --------------------------------------------------------------------------

/// <summary>
/// A typed, contiguous character span inside exactly one paragraph, owned by exactly one scene
/// (scene plan §3). The composition unit and the renderer's input — finer than a sentence,
/// because one sentence routinely mixes a <see cref="ItemKind.Speech"/> item and an
/// <see cref="ItemKind.Action"/> one.
/// </summary>
/// <param name="StartOffset">Half-open character offsets into the paragraph's frozen text (§3.3).</param>
/// <param name="IsStandalone">False when the span cannot be staged alone; set at phase 8, and the
/// selector for the <c>render-text</c> augmentation (§3.6).</param>
/// <param name="RenderText">Generated, never content, never hashed (§3.6). Null when standalone.</param>
public sealed record SceneItem(
    string ItemId,
    string ParagraphId,
    int StartOffset,
    int EndOffset,
    ItemKind Kind,
    SpeechAttributes? Speech,
    string? ExhibitId,
    IReadOnlyList<Tag> Tags,
    bool IsStandalone,
    string? RenderText,
    IReadOnlyList<string> EvidenceIds)
{
    public int Length => EndOffset - StartOffset;

    /// <summary>The item's own text, read out of the paragraph it names. Never stored on the item (C9).</summary>
    public string SpanOf(string paragraphText)
    {
        ArgumentNullException.ThrowIfNull(paragraphText);
        return StartOffset >= 0 && EndOffset <= paragraphText.Length && EndOffset >= StartOffset
            ? paragraphText[StartOffset..EndOffset]
            : string.Empty;
    }
}

/// <summary>
/// The two questions a renderer asks about an utterance, plus one field that is not a question
/// (scene plan §3.2). <see cref="SpeakerPersonaId"/> is null until phase 9 binds it (§8.4).
/// </summary>
public sealed record SpeechAttributes(
    string SurfaceForm,
    string? SpeakerPersonaId,
    Embodiment Embodiment,
    Addressee Addressee,
    bool Voiced)
{
    public static SpeechAttributes Dialogue(string surfaceForm) =>
        new(surfaceForm, null, Embodiment.Embodied, Addressee.InScene, Voiced: true);

    public static SpeechAttributes Narration(string surfaceForm) =>
        new(surfaceForm, null, Embodiment.Disembodied, Addressee.Audience, Voiced: true);
}

/// <summary>
/// A typed reference over a character range inside an item, resolved to a registry id
/// (scene plan §4). Offsets are relative to the <em>paragraph</em>, not the item, so they stay
/// verifiable against the hashed artifact and survive a re-cut that changed nothing a reader
/// could see (§4.1).
/// </summary>
public sealed record Tag(
    string TagId,
    TagKind Kind,
    int StartOffset,
    int EndOffset,
    string SurfaceForm,
    string? ReferentId,
    GroupMembership? Membership,
    double Confidence)
{
    /// <summary>True when this tag's range sits wholly inside <paramref name="other"/>'s.</summary>
    public bool NestsIn(Tag other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return StartOffset >= other.StartOffset && EndOffset <= other.EndOffset;
    }

    public bool IsDisjointFrom(Tag other)
    {
        ArgumentNullException.ThrowIfNull(other);
        return EndOffset <= other.StartOffset || other.EndOffset <= StartOffset;
    }
}

/// <summary>
/// Who "the brothers" resolved to <em>at this point in the document</em> (scene plan §4.3).
///
/// <para>It hangs off the <see cref="Tag"/> rather than off the <see cref="Persona"/> because a
/// group's identity persists across a document while its roster does not: two people in chapter 1
/// and three in chapter 20, with neither reference wrong. A registry field cannot express
/// that.</para>
/// </summary>
public sealed record GroupMembership(
    IReadOnlyList<string> MemberPersonaIds,

    /// <summary>The rendering switch: a complete group renders as its members, an opaque one as a crowd.</summary>
    bool IsComplete);

// ---- Registries (phase 9) ---------------------------------------------------------------------

/// <summary>
/// A registry entry for anyone who speaks, is addressed, or is present [unit §3.3]. Individuals
/// and groups alike; the narrator and <c>Unattributed</c> are personas.
/// </summary>
/// <param name="ScopeRootSectionId">Null in a single-family document; otherwise the family scope
/// this persona lives in and never merges out of (scene plan §6.1).</param>
/// <param name="Formation">Non-null iff <see cref="PersonaKind.Group"/>: identity, not membership.</param>
public sealed record Persona(
    string PersonaId,
    string CanonicalName,
    PersonaKind Kind,
    PersonaScope Scope,
    string? ScopeRootSectionId,
    IReadOnlyList<string> SurfaceForms,
    GroupFormation? Formation,
    IReadOnlyList<string> EvidenceIds);

/// <summary>A resolvable place in the run's place registry — what <see cref="Situation.PlaceId"/> names.</summary>
public sealed record PlaceEntry(
    string PlaceId,
    string CanonicalName,
    IReadOnlyList<string> SurfaceForms,
    IReadOnlyList<string> EvidenceIds);

/// <summary>A non-prose artifact, registered so an <see cref="TagKind.ExhibitRef"/> has something to bind to.</summary>
public sealed record ExhibitEntry(
    string ExhibitId,
    string Label,
    string ParagraphId,
    IReadOnlyList<string> EvidenceIds);

/// <summary>The three per-run id tables of scene plan §1, written whole by <c>RegistryMaterializer</c>.</summary>
public sealed record Registries(
    IReadOnlyList<Persona> Personas,
    IReadOnlyList<PlaceEntry> Places,
    IReadOnlyList<ExhibitEntry> Exhibits)
{
    public static readonly Registries Empty = new([], [], []);
}

// ---- Family scopes (evidence at phase 5, derived in code at phase 7) --------------------------

/// <summary>
/// A section whose subtree carries its own composition family (scene plan §6.1). Resolution for
/// any node is nearest ancestor-or-self with a scope, else the run's family — so a single-family
/// document produces none of these and pays one lookup that terminates at the root.
/// </summary>
public sealed record FamilyScope(
    string SectionId,
    string CompositionFamily,
    IReadOnlyList<string> EvidenceIds,
    double Confidence);

// ---- Scenes (phase 10) ------------------------------------------------------------------------

/// <summary>A coordinate's provenance and evidence [unit §8]. A nothing-value carries none: nothing was claimed.</summary>
public sealed record Provenance(
    CoordinateSource Source,
    string? InheritedFromSceneId,
    IReadOnlyList<string> EvidenceIds,
    string? Reason)
{
    public static Provenance Stated(IReadOnlyList<string> evidenceIds, string? reason = null) =>
        new(CoordinateSource.Stated, null, evidenceIds, reason);

    public static Provenance Inherited(string fromSceneId, IReadOnlyList<string> evidenceIds) =>
        new(CoordinateSource.Inherited, fromSceneId, evidenceIds, "carried from the preceding sibling scene");
}

/// <summary>One cast member and its role <em>in this scene</em> [unit §3.3].</summary>
public sealed record CastEntry(string PersonaId, CastRole Role, Provenance Provenance);

/// <summary>An anchor plus a continuity relation to the preceding scene [unit §3.2].</summary>
public sealed record TimeValue(string? Anchor, TimeRelation Relation, Provenance? Provenance)
{
    /// <summary>The nothing-value (C6). Expository material is normally this, and that is not a defect.</summary>
    public static readonly TimeValue Unanchored = new(null, TimeRelation.Unanchored, null);
}

/// <summary>What is happening or being treated — one coordinate, never two [unit §3.5].</summary>
public sealed record SubjectValue(string Text, Provenance? Provenance)
{
    public static readonly SubjectValue None = new(string.Empty, null);
}

/// <summary>
/// The five-coordinate tuple [unit §1]. Every coordinate is single-valued (C5) and always present
/// (C6) — <c>PlaceId = null</c> is Void, not a missing value, and there is no third state.
/// </summary>
public sealed record Situation(
    string? PlaceId,
    Provenance? SettingProvenance,
    TimeValue Time,
    IReadOnlyList<CastEntry> Cast,
    SceneMode Mode,
    SubjectValue Subject)
{
    public bool IsVoid => PlaceId is null;

    /// <summary>The always-legal answer of K7, for a span nothing else could be established about.</summary>
    public static Situation Exhibited(SubjectValue subject) =>
        new(null, null, TimeValue.Unanchored, [], SceneMode.Exhibited, subject);
}

/// <summary>A derived review signal — never a gate. Evaluated in code over the tuple [unit §3.1].</summary>
public sealed record Flag(string Kind, string Message)
{
    public const string UnlocatedEnactment = "unlocated-enactment";
    public const string OversizedScene = "oversized-scene";
    public const string DeepSettingInheritance = "deep-setting-inheritance";
}

/// <summary>
/// A typed link between two scenes (K4). <see cref="EvidenceIds"/> is never empty: an uncited link
/// is not materialized, because K4 is worth less than C11 (scene plan §8.9).
/// </summary>
public sealed record SceneLink(
    string FromSceneId,
    string ToSceneId,
    SceneLinkKind Kind,
    IReadOnlyList<string> EvidenceIds,
    double Confidence);

/// <summary>
/// The maximal contiguous run of items over which one situation holds [unit §1, §8].
///
/// <para><see cref="SceneId"/> derives from the first item's <c>(ParagraphId, StartOffset)</c>
/// rather than from a counter: two scenes may begin in one paragraph, so a bare paragraph id
/// cannot distinguish them, and anchoring to the hashed artifact is what makes the id survive a
/// re-run that re-cut the items but left the text alone (C10, K2).</para>
/// </summary>
public sealed record Scene(
    string SceneId,
    string SectionId,
    string FirstItemId,
    string LastItemId,
    IReadOnlyList<string> ItemIds,
    Situation Situation,
    IReadOnlyList<SceneLink> Links,
    string? Title,
    bool TitleInferred,
    IReadOnlyList<Flag> Flags,
    string ContentHash)
{
    /// <summary>How many scenes Setting was carried through to reach this one [unit §3.1.1].</summary>
    public int InheritanceDepth { get; init; }

    /// <summary>Paragraphs this scene touches, for C14's human-legible ceiling.</summary>
    public IReadOnlyList<string> ParagraphIds { get; init; } = [];
}

/// <summary>
/// What a link decision actually reads (scene plan §8.9): orders of magnitude smaller than the
/// scenes themselves, so a thousand-scene novel presents to the orchestrator as a digest list
/// rather than as a document.
/// </summary>
public sealed record SceneDigest(
    string SceneId,
    string SectionId,
    int Ordinal,
    string? Title,
    SceneMode Mode,
    TimeValue Time,
    IReadOnlyList<string> CastPersonaIds,
    string? PlaceId,
    string Subject,
    string? TransitionText);
