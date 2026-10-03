namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>The body of the conversion service's <c>POST /convert</c>.</summary>
public sealed record ConvertRequest(
    string UploadId,
    string SourceKey,
    string MarkdownKey,
    string LayoutKey,
    string ReportKey,
    string MediaType);
