using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Scheduling;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public sealed class AgentToolsFactory(
    IArtifactStore artifacts,
    IRunLedger ledger,
    IRegistry registry,
    IDocumentFamilyCatalog catalog,
    VerifierRunner verifiers,
    StageAttempts attempts,
    IOutcomeBus outcomes,
    TimeProvider time,
    EngineOptions options,
    IEnumerable<IExecutorSpecValidator> validators)
{
    public AgentTools ForRun(string runId, Signature signature, ArtifactRef input, TraceRef trace, CancellationToken ct) =>
        new(this, runId, signature, input, trace, scope: null, ct);

    /// <summary>Writes are not recorded: the scheduler records the whole loop as one attempt.</summary>
    public AgentTools ForStage(StageRequest request, TraceRef trace, CancellationToken ct) =>
        new(this, request.RunId, request.Signature, request.Inputs[0], trace, scope: request, ct);

    internal IArtifactStore Artifacts => artifacts;
    internal IRunLedger Ledger => ledger;
    internal IRegistry Registry => registry;
    internal IDocumentFamilyCatalog Catalog => catalog;
    internal VerifierRunner Verifiers => verifiers;
    internal StageAttempts Attempts => attempts;
    internal IOutcomeBus Outcomes => outcomes;
    internal TimeProvider Time => time;
    internal EngineOptions Options => options;
    internal IEnumerable<IExecutorSpecValidator> Validators => validators;
}
