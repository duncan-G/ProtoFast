namespace ProtoFast.DocumentImport.Engine;

public sealed record VerifierResult(
    string VerifierId,
    Verdict Verdict,
    string Reason,
    IReadOnlyList<Finding> Findings);
