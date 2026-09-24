namespace ProtoFast.Storage.Abstractions;

public sealed record ObjectRef(string Key, string Hash, long SizeBytes)
{
    public static readonly ObjectRef None = new(string.Empty, string.Empty, 0);

    public bool IsEmpty => Key.Length == 0;
}
