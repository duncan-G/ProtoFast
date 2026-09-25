using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

/// <summary>Fails content containing "bad", degrades content containing "meh".</summary>
internal sealed class ContentVerifier(string id, bool deterministic, IArtifactStore artifacts) : IVerifier
{
    public string Id => id;
    public bool IsDeterministic => deterministic;
    public ConcurrentQueue<ArtifactRef> Seen { get; } = new();

    public async Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct)
    {
        Seen.Enqueue(result.Output);
        using var reader = new StreamReader(await artifacts.GetAsync(result.Output, ct));
        var content = await reader.ReadToEndAsync(ct);
        var verdict = content.Contains("bad") ? Verdict.Fail : content.Contains("meh") ? Verdict.Degraded : Verdict.Pass;
        return new VerifierResult(id, verdict, verdict.ToString(), []);
    }
}
