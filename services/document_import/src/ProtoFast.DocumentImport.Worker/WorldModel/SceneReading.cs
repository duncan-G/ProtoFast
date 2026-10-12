namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>What the model returns for one scene, as the prompt asks for it.</summary>
public sealed class SceneReading
{
    public List<ReadPart> Parts { get; set; } = [];

    public List<ReadObject> Objects { get; set; } = [];

    public List<ReadAlias> Aliases { get; set; } = [];

    public List<ReadEdge> Edges { get; set; } = [];
}
