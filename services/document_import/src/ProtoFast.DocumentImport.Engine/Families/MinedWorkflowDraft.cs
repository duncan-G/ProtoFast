using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Families;

public sealed record MinedWorkflowDraft(WorkflowRef Ref, int Stages, DateTimeOffset MinedAt, bool Promoted);
