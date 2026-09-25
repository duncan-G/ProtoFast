namespace ProtoFast.DocumentImport.Engine;

public enum Verdict { Pass, Degraded, Fail }

public sealed record VerifierResult(
    string VerifierId,
    Verdict Verdict,
    string Reason,
    IReadOnlyList<Finding> Findings);

/// <summary>
/// Runs on every stage result regardless of tier. Deterministic and model-backed verifiers share
/// the interface.
/// </summary>
public interface IVerifier
{
    string Id { get; }
    bool IsDeterministic { get; }
    Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct);
}

/// <summary>
/// Builds the rubric-judged verifier for a <see cref="VerifierSpec"/> the agent defined in discovery
/// mode. Model-backed, so it lives outside the engine.
/// </summary>
public interface IRubricVerifierFactory
{
    IVerifier Create(VerifierSpec spec);
}
