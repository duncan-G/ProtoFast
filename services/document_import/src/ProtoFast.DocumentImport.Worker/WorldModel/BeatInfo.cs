using ProtoFast.Data.ThePlot.Entities;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>A beat of prose or dialogue; headings and transitions are not beats.</summary>
/// <param name="Cut">The transition just before the beat ("CUT TO"), when the scene cuts inside itself.</param>
public sealed record BeatInfo(
    Guid Id,
    SceneElementType Type,
    string Text,
    string? Speaker,
    string? Parenthetical,
    Guid? SpeakerId = null,
    string? Extension = null,
    string? Cut = null)
{
    /// <summary>Spoken over the scene by someone not in it.</summary>
    public bool IsVoiceOver => Extension is { } e && e.Replace(".", "").Replace("/", "").Replace(" ", "").Equals("VO", StringComparison.OrdinalIgnoreCase);
}
