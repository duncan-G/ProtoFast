namespace ProtoFast.DocumentImport.Data.Postgres.Entities;

public sealed class RegistryEntry
{
    public RegistryEntryKind Kind { get; set; }

    public required string Id { get; set; }

    public int Version { get; set; }

    public required string ContentHash { get; set; }

    public bool Promoted { get; set; }

    public DateTimeOffset PublishedAt { get; set; }
}
