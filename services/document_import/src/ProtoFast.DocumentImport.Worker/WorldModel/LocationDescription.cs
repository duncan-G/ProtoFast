namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>What the model returns for one location, as the prompt asks for it.</summary>
public sealed class LocationDescription
{
    public string? Description { get; set; }

    public string? Contents { get; set; }
}
