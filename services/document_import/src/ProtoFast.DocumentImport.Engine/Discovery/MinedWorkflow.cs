namespace ProtoFast.DocumentImport.Engine;

public sealed record MinedWorkflow(WorkflowDefinition Workflow, IReadOnlyList<PolicyRow> Seeds);
