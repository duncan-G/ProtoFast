using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.Data.ThePlot;

/// <summary>The times of day, transitions and character kinds a new story starts with.</summary>
public static class StoryVocabulary
{
    private static readonly string[] TimesOfDay = ["DAY", "NIGHT", "DAWN", "DUSK", "CONTINUOUS", "LATER"];

    private static readonly string[] Transitions = ["CUT TO", "DISSOLVE TO", "SMASH CUT TO", "MATCH CUT TO", "TIME CUT", "FADE OUT"];

    private static readonly string[] CharacterKinds = ["Human", "Robot", "Animal", "Creature", "Voice"];

    public static void AddDefaults(Story story)
    {
        story.TimesOfDay.AddRange(TimesOfDay.Select((label, i) => new TimeOfDay { Label = label, Position = i }));
        story.Transitions.AddRange(Transitions.Select((label, i) => new Transition { Label = label, Position = i }));
        story.CharacterKinds.AddRange(CharacterKinds.Select((label, i) => new CharacterKind { Label = label, Position = i }));
    }
}
