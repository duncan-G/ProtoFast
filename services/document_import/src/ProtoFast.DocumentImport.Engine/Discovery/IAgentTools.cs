namespace ProtoFast.DocumentImport.Engine;

public interface IAgentTools
{
    Task<DocumentFamilyContext> Context();
    Task<Stream>        ReadArtifact(ArtifactRef reference);

    // `inputs` become the stage's recorded dependencies.
    Task<WriteResult>   WriteArtifact(
        string stageId, Stream content, ContractRef contract, IReadOnlyList<ArtifactRef>? inputs = null);

    Task<ExecutorRef>   DefineExecutor(ExecutorSpec spec);
    Task<string>        DefineVerifier(VerifierSpec spec);

    // `output` defaults to the contract last recorded for the stage.
    Task<StageRecord>   Delegate(
        string stageId, ExecutorRef executor, IReadOnlyList<ArtifactRef> inputs, ContractRef? output = null);

    Task                Record(Decision decision);
}
