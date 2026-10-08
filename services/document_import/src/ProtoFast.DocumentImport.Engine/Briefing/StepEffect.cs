namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <summary>Something a tool call did, as the engine saw it happen.</summary>
/// <param name="Depth">0 for the call itself; a script's own calls to other skills are one deeper.</param>
/// <param name="Subject">What it acted on, for a reader to resolve: a skill as <c>id@version</c>, an artifact as <c>stage/hash</c>.</param>
public sealed record StepEffect(StepEffectKind Kind, string Summary, int Depth = 0, string? Subject = null);
