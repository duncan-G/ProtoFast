namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class DocumentFamilyExecutor
{
    public required string Family { get; set; }

    public required string ExecutorId { get; set; }

    public int ExecutorVersion { get; set; }

    public DateTimeOffset AddedAt { get; set; }
}
