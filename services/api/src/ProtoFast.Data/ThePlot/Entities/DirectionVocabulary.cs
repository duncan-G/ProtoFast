namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>The labels the director layer uses; delivery tags can grow per story in <see cref="StoryVocabulary"/>.</summary>
public static class DirectionVocabulary
{
    public static readonly IReadOnlyList<string> Shots = ["wide", "medium", "close", "extreme close", "over-the-shoulder", "insert"];

    public static readonly IReadOnlyList<string> Angles = ["eye level", "low", "high", "top-down"];

    public static readonly IReadOnlyList<string> Movements = ["static", "push in", "pull out", "pan", "handheld"];

    public static readonly IReadOnlyList<string> ScreenSides = ["left", "centre", "right", "off"];

    /// <summary>
    /// Which way a body is turned, as the viewer sees the picture, round the circle: a three-quarter
    /// turn shows the face, a back three-quarter turn shows the back.
    /// </summary>
    public static readonly IReadOnlyList<string> Facings =
    [
        "toward camera", "three-quarter left", "screen left", "back three-quarter left",
        "away from camera", "back three-quarter right", "screen right", "three-quarter right",
    ];

    /// <summary>Which way someone moves across the picture, as the viewer sees it.</summary>
    public static readonly IReadOnlyList<string> Movings =
    [
        "still", "left to right", "right to left", "toward camera", "away from camera",
        "toward camera, left to right", "toward camera, right to left",
        "away from camera, left to right", "away from camera, right to left",
    ];

    /// <summary>The way a character moves across the screen for a whole scene, so cuts match.</summary>
    public static readonly IReadOnlyList<string> Travels = ["left to right", "right to left"];

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
