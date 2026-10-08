using ProtoFast.DocumentImport.Engine.Briefing;

namespace ProtoFast.DocumentImport.Screenplay.Briefing;

internal sealed record OverviewReply(string? Outcome, string? Overview, IReadOnlyList<BriefFlag>? Flags);
