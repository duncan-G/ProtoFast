namespace ProtoFast.DocumentImport.Engine.Workflows;

public sealed class InvalidWorkflowException(WorkflowRef workflow, string reason)
    : Exception($"Workflow {workflow.Id}@{workflow.Version} is invalid: {reason}.")
{
    public WorkflowRef Workflow { get; } = workflow;
}
