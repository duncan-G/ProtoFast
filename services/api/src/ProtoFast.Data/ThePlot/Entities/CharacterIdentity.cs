namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// What never changes about a character within the story. Stored as JSON on the character; what
/// does change (wardrobe, injuries) lives on the scene's <see cref="SceneAppearance"/>.
/// </summary>
public sealed class CharacterIdentity
{
    public string Age { get; set; } = "";

    public string Gender { get; set; } = "";

    public string Build { get; set; } = "";

    public string Face { get; set; } = "";

    public string Hair { get; set; } = "";

    public string Skin { get; set; } = "";

    /// <summary>Scars, tattoos, glasses: anything a picture has to keep.</summary>
    public List<string> Marks { get; set; } = [];
}
