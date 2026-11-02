namespace ProtoFast.DocumentImport.Core;

/// <summary>
/// The queue message the API sends once an upload has landed and been recorded. The conversion
/// worker sends it back with <see cref="Conversion"/> set once a format that needs converting has been.
/// </summary>
public sealed record DocumentImportRequested(
    string UploadId,
    string UserId,
    string StorageKey,
    string FileName,
    string MediaType,
    string FileExtension,
    ConversionResult? Conversion = null);
