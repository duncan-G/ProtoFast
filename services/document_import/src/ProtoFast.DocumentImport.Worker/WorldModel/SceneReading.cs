namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>What the model returns for one scene, as the prompt asks for it.</summary>
public sealed class SceneReading
{
    /// <summary>Set earlier than the scene before it.</summary>
    public bool? Flashback { get; set; }

    public List<ReadPart> Parts { get; set; } = [];

    public List<ReadObject> Objects { get; set; } = [];

    public List<ReadAlias> Aliases { get; set; } = [];

    /// <summary>inside edges.</summary>
    public List<ReadEdge> Inside { get; set; } = [];

    /// <summary>next edges.</summary>
    public List<ReadEdge> Next { get; set; } = [];

    public List<ReadBeat> Beats { get; set; } = [];

    /// <summary>Edges given outside a beat, each naming its own beat.</summary>
    public List<ReadEdge> Edges { get; set; } = [];

    /// <summary>Every edge, each labelled with its kind and beat.</summary>
    public IEnumerable<ReadEdge> AllEdges()
    {
        foreach (var edge in Inside)
        {
            edge.Edge = "inside";
            yield return edge;
        }

        foreach (var edge in Next)
        {
            edge.Edge = "next";
            yield return edge;
        }

        foreach (var beat in Beats)
        {
            if (beat.Action is { } action)
            {
                yield return Of(beat, action, "beat_at");
            }

            if (beat.Camera is { } camera)
            {
                yield return Of(beat, camera, "camera");
            }

            foreach (var character in beat.Characters ?? [])
            {
                yield return Of(beat, character, "character_at");
            }

            foreach (var edge in beat.Edges ?? [])
            {
                edge.BeatId ??= beat.BeatId;
                yield return edge;
            }
        }

        foreach (var edge in Edges)
        {
            yield return edge;
        }
    }

    private static ReadEdge Of(ReadBeat beat, ReadEdge edge, string kind)
    {
        edge.BeatId ??= beat.BeatId;
        edge.Edge = kind;
        return edge;
    }
}
