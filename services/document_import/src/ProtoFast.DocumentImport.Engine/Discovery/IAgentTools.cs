using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public interface IAgentTools
{
    Task<DocumentFamilyContext> Context();
    Task<Stream>        ReadArtifact(ArtifactRef reference);

    // `inputs` become the stage's recorded dependencies; `cost` is what the agent spent producing it.
    Task<WriteResult>   WriteArtifact(
        string stageId, Stream content, ContractRef contract, IReadOnlyList<ArtifactRef>? inputs = null,
        Cost? cost = null);

    Task<PlaybookRef>   DefinePlaybook(Playbook playbook);

    // Returns the hash to set as ExecutorSpec.CodeAssembly.
    Task<string>        UploadCode(Stream code);
    Task<Stream>        ReadCode(string hash);

    /// <summary>This document family's skills, the latest version of each.</summary>
    Task<IReadOnlyList<Skill>> Skills();

    // Publishes the next version of the skill's id; its scripts must name uploaded code.
    Task<SkillRef>      DefineSkill(Skill skill);

    // Hides every version from later runs; the family's history keeps them.
    Task                RemoveSkill(string id, string reason);

    Task<ExecutorRef>   DefineExecutor(ExecutorSpec spec);
    Task<string>        DefineVerifier(VerifierSpec spec);

    // `output` defaults to the contract last recorded for the stage.
    Task<StageRecord>   Delegate(
        string stageId, ExecutorRef executor, IReadOnlyList<ArtifactRef> inputs, ContractRef? output = null);

    Task                Record(Decision decision);

    /// <summary>What this run has recorded so far, in order; what an interrupted run resumes from.</summary>
    Task<IReadOnlyList<StageRecord>> Records();

    // The run's journal of model messages, so a run interrupted by an outage or a crash resumes
    // instead of restarting. Empty, and appends are dropped, in a stage-scoped loop.
    Task<IReadOnlyList<string>> LoadTranscript();
    Task                AppendTranscript(int sequence, string json);

    // Dropped in a stage-scoped loop, like the transcript.
    Task                RecordSystemPrompt(int fromSequence, string prompt);

    // What a model turn's tool calls did. Dropped in a stage-scoped loop, like the transcript.
    Task                RecordStep(RunStep step);
}
