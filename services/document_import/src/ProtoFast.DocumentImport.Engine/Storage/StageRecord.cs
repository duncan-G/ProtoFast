using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Storage;

/// <summary>
/// In discovery mode <see cref="Stage"/> is recorded from the agent's call; its DependsOn is the
/// recorded DAG.
/// </summary>
public sealed record StageRecord(
    string RunId, StageDefinition Stage, IReadOnlyList<ArtifactRef> Inputs, ExecutorRef Executor, Tier Tier,
    StageResult Result, IReadOnlyList<VerifierResult> Verdicts, bool IsShadow)
{
    public string StageId => Stage.Id;
    public ArtifactRef Output => Result.Output;
    public bool Passed => Verdicts.All(v => v.Verdict != Verdict.Fail);
    public bool Degraded => Passed && Verdicts.Any(v => v.Verdict == Verdict.Degraded);
}
