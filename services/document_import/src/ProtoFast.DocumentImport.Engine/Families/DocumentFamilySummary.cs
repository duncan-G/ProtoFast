using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Families;

/// <param name="Info">Null for a family nothing was written about: one only the ledger knows.</param>
/// <param name="Generation">The current one; mode, workflow and the learned counts are its, run counts span every generation.</param>
public sealed record DocumentFamilySummary(
    string Family,
    DocumentFamilyInfo? Info,
    int Generation,
    RunMode Mode,
    WorkflowRef? Workflow,
    int Runs,
    int OpenRuns,
    DateTimeOffset? LastRunAt,
    int Skills,
    int Executors,
    int Verifiers);
