using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

/// <summary>Fails a skill whose instructions or scripts mention "Sweetfoot".</summary>
internal sealed class ContentSkillVerifier : ISkillVerifier
{
    public string Id => "no-sweetfoot";
    public bool IsDeterministic => true;

    public Task<VerifierResult> VerifyAsync(SkillReview review, CancellationToken ct)
    {
        var tied = new List<Finding>();
        if (review.Skill.Instructions.Contains("Sweetfoot"))
        {
            tied.Add(new Finding("instructions", "names Sweetfoot"));
        }

        tied.AddRange(review.Sources.Where(s => s.Value.Contains("Sweetfoot")).Select(s => new Finding($"scripts/{s.Key}", "names Sweetfoot")));
        return Task.FromResult(tied.Count > 0
            ? new VerifierResult(Id, Verdict.Fail, "Tied to Sweetfoot.", tied)
            : new VerifierResult(Id, Verdict.Pass, "General.", []));
    }
}
