namespace ProtoFast.DocumentImport.Engine;

public readonly record struct ArtifactRef(string RunId, string StageId, string Hash)
{
    public const string InputStageId = "$input";

    public static readonly ArtifactRef None = new(string.Empty, string.Empty, string.Empty);

    public bool IsNone => string.IsNullOrEmpty(Hash);
}
