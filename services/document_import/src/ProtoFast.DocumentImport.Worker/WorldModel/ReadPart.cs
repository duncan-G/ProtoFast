namespace ProtoFast.DocumentImport.Worker.WorldModel;

public sealed class ReadPart
{
    /// <summary>How the reply's edges name this new part.</summary>
    public string? Ref { get; set; }

    public string? Name { get; set; }

    /// <summary>A part id, or the ref of another new part.</summary>
    public string? ParentPartId { get; set; }

    public string? Description { get; set; }

    public int? Position { get; set; }
}
