namespace ProtoFast.DocumentImport.Worker.WorldModel;

public sealed class ReadObject
{
    public string? Ref { get; set; }

    public string? Name { get; set; }

    public List<string>? Aliases { get; set; }

    public string? Kind { get; set; }

    public string? Description { get; set; }

    /// <summary>The name of the story prop it is, when it is one.</summary>
    public string? Prop { get; set; }
}
