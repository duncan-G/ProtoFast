using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public sealed record MinedWorkflow(WorkflowDefinition Workflow, IReadOnlyList<PolicyRow> Seeds);
