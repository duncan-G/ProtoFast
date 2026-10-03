namespace ProtoFast.DocumentImport.Core;

/// <summary>The queue message the API sends once an upload has landed and been recorded.</summary>
public sealed record DocumentImportRequested(
    string UploadId,
    string UserId,
    string StorageKey,
    string FileName,
    string MediaType,
    string FileExtension);
