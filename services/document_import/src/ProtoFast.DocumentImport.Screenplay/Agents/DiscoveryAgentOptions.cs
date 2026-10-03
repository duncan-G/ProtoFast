using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

public sealed class DiscoveryAgentOptions
{
    public string ModelClass { get; set; } = ModelClasses.Medium;

    /// <summary>Model turns before the loop gives up on a run.</summary>
    public int MaxTurns { get; set; } = 120;

    /// <summary>Tool results beyond this are cut before they reach the model.</summary>
    public int MaxResultChars { get; set; } = 60_000;
}
