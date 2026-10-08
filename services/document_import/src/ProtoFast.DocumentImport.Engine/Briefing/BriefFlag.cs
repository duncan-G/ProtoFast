namespace ProtoFast.DocumentImport.Engine.Briefing;

/// <summary>Something a reviewer should look at.</summary>
/// <param name="Sequence">The step it is about; null when it is about the whole run.</param>
public sealed record BriefFlag(string Kind, string Detail, int? Sequence);
