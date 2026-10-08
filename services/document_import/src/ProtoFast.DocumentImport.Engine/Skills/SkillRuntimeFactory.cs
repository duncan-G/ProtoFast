using ProtoFast.DocumentImport.Engine.Discovery;

namespace ProtoFast.DocumentImport.Engine.Skills;

public sealed class SkillRuntimeFactory(ScriptCompiler compiler, EngineOptions options, IEnumerable<IScriptSafetyReviewer> reviewers)
{
    private readonly IReadOnlyList<IScriptSafetyReviewer> _reviewers = reviewers.ToList();

    public SkillRuntime Create(IAgentTools tools, CancellationToken ct) => new(tools, compiler, _reviewers, options, ct);
}
