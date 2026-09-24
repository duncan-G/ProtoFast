namespace ProtoFast.DocumentImport.Core;

public sealed record SourceFormat(
    string Extension,
    string MediaType,
    string Label,
    bool ProducesLayout,
    bool OcrCapable,
    bool RequiresConversion,
    IReadOnlyList<string> Aliases)
{
    public bool Accepts(string mediaType) =>
        string.Equals(MediaType, mediaType, StringComparison.OrdinalIgnoreCase)
        || Aliases.Contains(mediaType, StringComparer.OrdinalIgnoreCase);
}
