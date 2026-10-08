using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Screenplay.Verifiers;

public sealed class ScriptSafetyOptions
{
    /// <summary>Always on Anthropic, whatever provider the agent runs on.</summary>
    public string ModelClass { get; set; } = ModelClasses.Large;
}
