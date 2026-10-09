namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A character in one scene: whether panels show them, and a wardrobe from their sheet plus what the
/// scene changes about it and about them.
/// Owned by its <see cref="Scene"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class SceneAppearance
{
    public Guid Id { get; set; }

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    /// <summary>On screen in every panel of the scene unless a panel says otherwise.</summary>
    public bool OnScreen { get; set; } = true;

    /// <summary>Null wears the sheet's first wardrobe, its default.</summary>
    public Guid? WardrobeId { get; set; }

    public CharacterWardrobe? Wardrobe { get; set; }

    /// <summary>On top of the wardrobe's description: "hood up, damp from the drizzle".</summary>
    public string WardrobeChange { get; set; } = "";

    /// <summary>On top of the sheet's description: "left hand bandaged".</summary>
    public string AppearanceChange { get; set; } = "";

    /// <summary>Why this differs from the character's previous look.</summary>
    public string? ChangeReason { get; set; }

    public Guid? ChangeElementId { get; set; }

    public SceneElement? ChangeElement { get; set; }

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }
}
