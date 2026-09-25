namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// What a <see cref="SceneElement"/> is, and so which of its optional columns apply.
/// Stored by name, so members can be reordered or added without a data migration; renaming one does need one.
/// </summary>
public enum SceneElementType
{
    /// <summary>A scene heading: location plus time of day. The editor labels it "Location".</summary>
    Heading,

    /// <summary>What happens.</summary>
    Action,

    /// <summary>What it looks, sounds, feels like.</summary>
    Description,

    /// <summary>The narrator's voice.</summary>
    Narration,

    /// <summary>A line spoken by a cast member.</summary>
    Dialogue,

    /// <summary>How the flow moves on.</summary>
    Transition,
}
