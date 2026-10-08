using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Briefing;

public sealed record BriefCandidate(string RunId, string Family, RunMode Mode, RunStatus Status, string? Failure);
