using ProtoFast.DocumentImport.Core;

namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>A message on the conversion worker's queue; it sends <see cref="ImportRequest"/> back with the outcome.</summary>
public sealed record ConversionRequested(
    string UploadId,
    string SourceKey,
    string MarkdownKey,
    string LayoutKey,
    string ReportKey,
    string MediaType,
    DocumentImportRequested ImportRequest);
