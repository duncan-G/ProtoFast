namespace ProtoFast.DocumentImport.Worker.Import;

public sealed class ConversionOptions
{
    /// <summary>The conversion service; unset means uploads are read as text without converting.</summary>
    public string? BaseUrl { get; set; }
}
