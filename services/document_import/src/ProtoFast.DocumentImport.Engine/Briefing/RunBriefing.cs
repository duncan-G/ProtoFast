namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <param name="Brief">Set once the status is Briefed.</param>
public sealed record RunBriefing(
    RunBriefStatus Status, int Attempts, RunBrief? Brief, string? Error, DateTimeOffset UpdatedAt);
