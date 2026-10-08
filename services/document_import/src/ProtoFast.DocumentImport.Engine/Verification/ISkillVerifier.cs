namespace ProtoFast.DocumentImport.Engine.Verification;

/// <summary>Judges a skill before it is published to its document family, whatever the family.</summary>
public interface ISkillVerifier
{
    string Id { get; }
    bool IsDeterministic { get; }
    Task<VerifierResult> VerifyAsync(SkillReview review, CancellationToken ct);
}
