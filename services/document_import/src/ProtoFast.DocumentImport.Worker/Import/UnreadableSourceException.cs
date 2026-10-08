namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>The upload can't be turned into text, so redelivering it would fail the same way.</summary>
public sealed class UnreadableSourceException(string storageKey, string reason, Exception? inner = null)
    : Exception($"{storageKey} can't be read: {reason}.", inner)
{
    public string StorageKey { get; } = storageKey;
}
