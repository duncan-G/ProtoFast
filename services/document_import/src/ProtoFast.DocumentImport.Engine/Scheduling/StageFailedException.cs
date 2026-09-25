using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;

namespace ProtoFast.DocumentImport.Engine.Scheduling;

public sealed class StageFailedException(StageRequest request, IReadOnlyList<VerifierResult> verdicts)
    : Exception(
        $"Stage '{request.Stage.Id}' of run {request.RunId} failed at Orchestrator: "
        + string.Join("; ", verdicts.Where(v => v.Verdict == Verdict.Fail).Select(v => $"{v.VerifierId}: {v.Reason}")))
{
    public StageRequest Request { get; } = request;
    public IReadOnlyList<VerifierResult> Verdicts { get; } = verdicts;
}
