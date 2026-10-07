using ProtoFast.DocumentImport.Engine.Policy;

namespace ProtoFast.DocumentImport.Engine.Families;

/// <param name="Generation">The generation shown; policy and everything learned below are keyed to it.</param>
/// <param name="RunsByGeneration">Every generation that has runs, oldest first.</param>
public sealed record DocumentFamilyDetail(
    string Family,
    DocumentFamilyInfo? Info,
    int CurrentGeneration,
    DateTimeOffset? ResetAt,
    int Generation,
    DocumentFamilyPolicy Policy,
    IReadOnlyList<FamilySkill> Skills,
    IReadOnlyList<FamilyExecutor> Executors,
    IReadOnlyList<FamilyVerifier> Verifiers,
    IReadOnlyList<PolicyRow> StagePolicies,
    IReadOnlyList<MinedWorkflowDraft> MinedWorkflows,
    IReadOnlyList<GenerationRuns> RunsByGeneration);
