namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>What one beat gives: where its action is, where the camera stands, who is where, and its other edges.</summary>
public sealed class ReadBeat
{
    public string? BeatId { get; set; }

    /// <summary>A beat_at edge.</summary>
    public ReadEdge? Action { get; set; }

    public ReadEdge? Camera { get; set; }

    /// <summary>character_at edges.</summary>
    public List<ReadEdge>? Characters { get; set; }

    /// <summary>at, state and connects edges.</summary>
    public List<ReadEdge>? Edges { get; set; }
}
