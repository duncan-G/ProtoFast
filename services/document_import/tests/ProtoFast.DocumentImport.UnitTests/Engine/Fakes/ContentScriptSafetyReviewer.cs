using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

/// <summary>Refuses a script that mentions "exfiltrate" and faults on one that mentions "unreviewable".</summary>
internal sealed class ContentScriptSafetyReviewer : IScriptSafetyReviewer
{
    public ConcurrentQueue<ScriptSafetyReview> Reviews { get; } = new();

    public Task<ScriptSafetyVerdict> ReviewAsync(ScriptSafetyReview review, CancellationToken ct)
    {
        Reviews.Enqueue(review);
        if (review.Source.Contains("unreviewable"))
        {
            throw new HttpRequestException("The reviewer is down.");
        }

        return Task.FromResult(review.Source.Contains("exfiltrate")
            ? new ScriptSafetyVerdict(false, "It copies data out of the run.", [new Finding("line 5", "exfiltrates")], new Cost(0.02m, TimeSpan.Zero))
            : new ScriptSafetyVerdict(true, "Pure computation.", [], Cost.Zero));
    }
}
