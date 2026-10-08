namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <param name="Briefing">Null until the briefer has claimed the run.</param>
public sealed record RunReview(IReadOnlyList<RunStep> Steps, RunBriefing? Briefing);
