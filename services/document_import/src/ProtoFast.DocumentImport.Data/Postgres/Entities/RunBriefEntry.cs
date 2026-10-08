using ProtoFast.DocumentImport.Engine.Briefing;

namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class RunBriefEntry
{
    public required string RunId { get; set; }
    public RunBriefStatus Status { get; set; }
    public int Attempts { get; set; }
    public DateTimeOffset ClaimedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public string? Brief { get; set; }
    public string? Error { get; set; }
}
