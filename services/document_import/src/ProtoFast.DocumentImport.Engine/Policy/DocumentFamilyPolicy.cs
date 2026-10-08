using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Policy;

public sealed record DocumentFamilyPolicy(
    string Family,
    RunMode Mode,
    WorkflowRef? Workflow,
    Confidence Confidence,
    DateTimeOffset UpdatedAt)
{
    public static DocumentFamilyPolicy Default(string family, DateTimeOffset at) =>
        new(family, RunMode.Discovery, null, Confidence.Prior, at);
}
