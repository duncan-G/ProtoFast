using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

public sealed class SkillVerificationOptions
{
    public string JudgeModelClass { get; set; } = ModelClasses.Medium;
}
