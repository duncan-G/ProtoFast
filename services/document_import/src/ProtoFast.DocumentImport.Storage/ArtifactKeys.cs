namespace ProtoFast.DocumentImport.Storage;

public static class ArtifactKeys
{
    public const string UploadsPrefix = "uploads/";

    public const string RunsPrefix = "runs/";

    public static string UploadSource(string ownerSubject, string uploadId, string extension) =>
        $"{UploadsPrefix}{ownerSubject}/{uploadId}{extension}";

    public static string RunArtifact(string runId, string stageId, string hash) =>
        $"{RunsPrefix}{Segment(runId)}/{Segment(stageId)}/{Segment(hash)}";

    public static string RunArtifactContract(string runId, string stageId, string hash) =>
        $"{RunArtifact(runId, stageId, hash)}.contract.json";

    // Stage ids come from the agent; escaping stops one from adding path segments.
    private static string Segment(string value) => Uri.EscapeDataString(value);
}
