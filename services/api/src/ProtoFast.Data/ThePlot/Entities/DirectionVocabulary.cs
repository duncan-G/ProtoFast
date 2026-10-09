namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>The labels the director layer uses; delivery tags can grow per story in <see cref="StoryVocabulary"/>.</summary>
public static class DirectionVocabulary
{
    public static readonly IReadOnlyList<string> Shots = ["wide", "medium", "close", "extreme close", "over-the-shoulder", "insert"];

    public static readonly IReadOnlyList<string> Angles = ["eye level", "low", "high", "top-down"];

    public static readonly IReadOnlyList<string> Movements = ["static", "push in", "pull out", "pan", "handheld"];

    public static readonly IReadOnlyList<string> ScreenSides = ["left", "centre", "right", "off"];

    /// <summary>Emotion, delivery and non-verbal tags, as the audio model documents them.</summary>
    public static readonly IReadOnlyList<string> DeliveryTags =
    [
        "sad", "angry", "happy", "excited", "nervous", "calm", "tired", "afraid", "curious", "sarcastic",
        "playful", "serious", "warm", "cold", "tender", "bitter", "hopeful", "resigned", "amused", "annoyed",
        "whispers", "shouts", "quietly", "softly", "slowly", "quickly", "hesitates", "stammers", "firmly",
        "flatly", "deadpan", "pleading", "mocking", "urgent",
        "laughs", "chuckles", "sighs", "gasps", "cries", "sobs", "clears throat", "coughs", "sniffs",
        "exhales", "inhales", "pause", "long pause", "beat",
    ];
}
