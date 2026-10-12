namespace ProtoFast.DocumentImport.Core;

public static class WorldModelQueues
{
    /// <summary>The keyed <c>IMessageQueue</c> that carries <see cref="WorldModelBuildRequested"/>.</summary>
    public const string BuildQueueKey = "world-model-build";
}
