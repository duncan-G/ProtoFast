using ProtoFast.DocumentImport.Engine.Briefing;

namespace ProtoFast.DocumentImport.Screenplay.Briefing;

internal sealed record StepBriefsReply(IReadOnlyList<StepBrief>? Steps);
