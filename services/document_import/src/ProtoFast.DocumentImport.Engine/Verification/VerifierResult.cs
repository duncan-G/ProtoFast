namespace ProtoFast.DocumentImport.Engine.Verification;

public sealed record VerifierResult(
    string VerifierId,
    Verdict Verdict,
    string Reason,
    IReadOnlyList<Finding> Findings);
