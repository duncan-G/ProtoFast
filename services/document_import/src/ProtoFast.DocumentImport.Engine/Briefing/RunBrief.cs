namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <summary>What a model wrote about a finished run so a person can review it without reading the conversation.</summary>
/// <param name="Steps">Only the steps whose recorded effects do not speak for themselves.</param>
/// <param name="Cost">USD the briefing itself spent; never part of the run's cost.</param>
public sealed record RunBrief(
    string Outcome,
    string Overview,
    IReadOnlyList<BriefFlag> Flags,
    IReadOnlyList<StepBrief> Steps,
    string ModelId,
    decimal Cost);
