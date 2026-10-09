namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>How a character sounds. Stored as JSON on the character.</summary>
public sealed class VoiceProfile
{
    /// <summary>"late 50s, gravelly, slow, Glaswegian"</summary>
    public string Description { get; set; } = "";

    /// <summary>The delivery a line without tags falls back to, e.g. "dry, understated".</summary>
    public string Baseline { get; set; } = "";

    /// <summary>The speech provider's voice for this character; null takes the provider's default.</summary>
    public string? VoiceId { get; set; }
}
