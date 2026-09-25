namespace ProtoFast.DocumentImport.Engine;

public sealed record WorkflowDefinition(WorkflowRef Ref, IReadOnlyList<StageDefinition> Stages)
{
    public IReadOnlyList<StageDefinition> TerminalStages =>
        Stages.Where(s => !Stages.Any(o => o.DependsOn.Contains(s.Id))).ToList();
}
