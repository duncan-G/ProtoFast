namespace ProtoFast.Segmentation.Core.Model;

// The enumerations of the scene plan §10. They live beside the pipeline's own enums rather than
// under Scenes/ because three layers read them — items, scenes and the registries — and a type
// every layer names belongs to none of them in particular.

/// <summary>
/// Whether a paragraph is part of the displayable work (scene plan §5). Metadata is withheld from
/// the scene stream and never deleted, which is why this is a classification rather than a filter.
/// </summary>
public enum Presentation
{
    Displayable,
    Metadata,
}

/// <summary>The five metadata classes of scene plan §5.1.</summary>
public enum MetadataClass
{
    RunningApparatus,
    FrontMatter,
    BackMatter,
    NavigationalLabel,
    ProductionNoise,
}

/// <summary>
/// The five item kinds (scene plan §3.1). A kind exists only where a renderer does something
/// different with it, which is the test that keeps the list at five.
/// </summary>
public enum ItemKind
{
    /// <summary>An utterance attributed to a persona — the only kind that produces audio from a voice.</summary>
    Speech,

    /// <summary>Something that happens; an event in time. Animates the frame.</summary>
    Action,

    /// <summary>The state of a place, person or thing. Sets the frame — the opposite of Action.</summary>
    Description,

    /// <summary>A non-prose artifact shown: figure, table, equation, code, media, with its caption.</summary>
    Exhibit,

    /// <summary>The explicit marker of the shift into this scene. The title-card text.</summary>
    Transition,
}

/// <summary>Does a mouth appear on screen (scene plan §3.2).</summary>
public enum Embodiment
{
    Embodied,
    Disembodied,
}

/// <summary>Does anyone in the world react (scene plan §3.2).</summary>
public enum Addressee
{
    InScene,
    Audience,
    Self,
}

/// <summary>The four tag kinds; the list is closed for v1 [unit §9].</summary>
public enum TagKind
{
    Persona,
    Group,
    Place,

    /// <summary>A pointer to an <see cref="ItemKind.Exhibit"/>, not the artifact itself (scene plan §4.2).</summary>
    ExhibitRef,
}

public enum PersonaKind
{
    Individual,

    /// <summary>"the brothers", "the crowd" — a persona, not a separate referent type (scene plan §4.3).</summary>
    Group,
}

public enum PersonaScope
{
    Persistent,
    SceneLocal,
}

/// <summary>
/// How a group reference resolves. <see cref="Enumerated"/> is deterministic from a conjunction of
/// already-tagged individuals; <see cref="Named"/> needs the registry and an agent (scene plan §4.3).
/// </summary>
public enum GroupFormation
{
    Enumerated,
    Named,
}

/// <summary>The five modes [unit §3.4]. Mode is live in every mode, so a mode change is always a cut.</summary>
public enum SceneMode
{
    Enacted,
    Narrated,
    Expounded,
    Addressed,

    /// <summary>The always-available answer: apparatus shown with no agent (C11, K7).</summary>
    Exhibited,
}

/// <summary>A scene's continuity relation to the one before it [unit §3.2].</summary>
public enum TimeRelation
{
    Continuous,
    Gap,
    Earlier,
    Later,
    Simultaneous,
    Unanchored,
}

/// <summary>A persona's role in one scene [unit §3.3]. Mention is not presence.</summary>
public enum CastRole
{
    Speaking,
    Addressed,
    Present,
}

/// <summary>Where a coordinate's value came from [unit §8]. Inherited values carry their source scene (C7).</summary>
public enum CoordinateSource
{
    Stated,
    Inferred,
    Inherited,
}

/// <summary>The typed links of K4 (scene plan §8.9).</summary>
public enum SceneLinkKind
{
    /// <summary>The C12 split: derived in code at phase 10, never by a model.</summary>
    Continues,
    ReturnsTo,
    FlashbackOf,
    ConcurrentWith,
    Frames,
    FramedBy,
}

/// <summary>The two independent family axes of scene plan §6. One field cannot carry both.</summary>
public enum FamilyAxis
{
    /// <summary>How the file was made: cleaning, running apparatus, OCR repair, layout trust.</summary>
    Production,

    /// <summary>What kind of work it is: metadata policy, scene cutting, item typing, persona scope.</summary>
    Composition,
}

/// <summary>
/// Which agent may see an instinct (scene plan §7.1). Without it every consumer would draw from
/// one pool and spend its six-instinct budget on guidance meant for another agent.
/// </summary>
public enum InstinctScope
{
    Labeling,
    Structure,
    Metadata,
    ItemType,
    SceneCut,
    Persona,
}

/// <summary>
/// What one augmentation call covers (scene plan §10). Granularity is a property of the type, not
/// of the pipeline — which is what lets the re-writer be an item-scoped type rather than a phase.
/// </summary>
public enum AugmentationScope
{
    Paragraph,
    Item,
    Scene,
    Section,
    Persona,
}
