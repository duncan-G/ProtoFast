namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>The labels every story offers, ahead of its own <see cref="StoryVocabulary"/>.</summary>
public static class DefaultVocabulary
{
    public static readonly IReadOnlyList<string> TimesOfDay =
        ["DAY", "NIGHT", "DAWN", "DUSK", "CONTINUOUS", "LATER"];

    public static readonly IReadOnlyList<string> Transitions =
        ["CUT TO", "DISSOLVE TO", "SMASH CUT TO", "MATCH CUT TO", "TIME CUT", "FADE OUT"];

    /// <summary>Character cue extensions, printed in parentheses after the speaker.</summary>
    public static readonly IReadOnlyList<string> Extensions = ["V.O.", "O.S.", "O.C."];

    public static readonly IReadOnlyList<CharacterKind> CharacterKinds =
    [
        new() { Label = "Human", AvatarShape = AvatarShape.Circle },
        new() { Label = "Robot", AvatarShape = AvatarShape.Square },
        new() { Label = "Animal", AvatarShape = AvatarShape.Teardrop },
        new() { Label = "Creature", AvatarShape = AvatarShape.Squircle },
        new() { Label = "Voice", AvatarShape = AvatarShape.Circle },
    ];
}
