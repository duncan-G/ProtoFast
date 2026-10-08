using ProtoFast.DocumentImport.Engine.Discovery;

namespace ProtoFast.DocumentImport.Engine.Skills;

public sealed class SkillRuntimeFactory(ScriptCompiler compiler, EngineOptions options)
{
    public SkillRuntime Create(IAgentTools tools, CancellationToken ct) => new(tools, compiler, options, ct);
}
