namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A character's look from one line of the scene on: they pick something up or hand it over, change
/// clothes, get hurt. It holds the whole look, so the frame covering the line and every frame after
/// it show it, and the next scene opens with the last one.
/// Owned by its <see cref="Scene"/>: it has no <c>UserId</c> and is only read and written through it.
/// </summary>
public sealed class SceneLookChange
{
    public Guid Id { get; set; }

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public Guid CharacterId { get; set; }

    public Character Character { get; set; } = null!;

    public Guid ElementId { get; set; }

    public SceneElement Element { get; set; } = null!;

    /// <summary>Null wears the sheet's first wardrobe, its default.</summary>
    public Guid? WardrobeId { get; set; }

    public CharacterWardrobe? Wardrobe { get; set; }

    public string WardrobeChange { get; set; } = "";

    public string AppearanceChange { get; set; } = "";

    /// <summary>The props they carry from this line on.</summary>
    public List<Guid> Holding { get; set; } = [];

    /// <summary>"hands the cooler to the doctor"</summary>
    public string? Reason { get; set; }

    /// <summary>Written by the author; a later directing pass leaves it alone.</summary>
    public bool Edited { get; set; }

    /// <summary>The look pictured, when it differs from the library's; frames take it as their reference.</summary>
    public Guid? ImageAssetId { get; set; }

    public MediaAsset? Image { get; set; }
}
