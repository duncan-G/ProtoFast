using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

public sealed record RubricJudgement(string? Verdict, string? Reason, IReadOnlyList<Finding>? Findings);
