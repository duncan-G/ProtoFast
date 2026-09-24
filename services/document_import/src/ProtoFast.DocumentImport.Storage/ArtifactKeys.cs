namespace ProtoFast.DocumentImport.Storage;

public static class ArtifactKeys
{
    public const string UploadsPrefix = "uploads/";

    public static string UploadSource(string ownerSubject, string uploadId, string extension) =>
        $"{UploadsPrefix}{ownerSubject}/{uploadId}{extension}";
}
