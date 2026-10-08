using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

public sealed record ScriptSafetyJudgement(bool? Safe, string? Reason, IReadOnlyList<Finding>? Findings);
