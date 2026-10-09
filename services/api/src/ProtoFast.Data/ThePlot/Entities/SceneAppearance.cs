namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A character in one scene: whether frames show them, and their look as the scene opens: a wardrobe
/// from their sheet, what is different about it and about them, and what they carry. What changes
/// during the scene is a <see cref="SceneLookChange"/>.
/// Owned by its <see cref="Scene"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class SceneAppearance
{
    public Guid Id { get; set; }

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    /// <summary>On screen in every frame of the scene unless a frame says otherwise.</summary>
    public bool OnScreen { get; set; } = true;

    /// <summary>Null wears the sheet's first wardrobe, its default.</summary>
    public Guid? WardrobeId { get; set; }

    public CharacterWardrobe? Wardrobe { get; set; }

    /// <summary>On top of the wardrobe's description: "hood up, damp from the drizzle".</summary>
    public string WardrobeChange { get; set; } = "";

    /// <summary>On top of the sheet's description: "left hand bandaged".</summary>
    public string AppearanceChange { get; set; } = "";

    /// <summary>The props they carry: in hand, under an arm, slung over a shoulder.</summary>
    public List<Guid> Holding { get; set; } = [];

    /// <summary>Why this differs from the character's previous look.</summary>
    public string? ChangeReason { get; set; }

    public Guid? ChangeElementId { get; set; }

    public SceneElement? ChangeElement { get; set; }

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }

    /// <summary>The look pictured, when it differs from the library's; frames take it as their reference.</summary>
    public Guid? ImageAssetId { get; set; }

    public MediaAsset? Image { get; set; }
}
