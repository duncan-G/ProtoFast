namespace ProtoFast.DocumentImport.Engine.Discovery;

/// <summary>
/// The run itself failed - the agent ran out of turns, stopped short, or was refused - rather than
/// the infrastructure under it. The run is abandoned and the import is not retried.
/// </summary>
public sealed class DiscoveryFailedException(string message, Exception? inner = null) : Exception(message, inner);
