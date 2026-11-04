using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>A resume or CV file; one can go with any number of applications.</summary>
public sealed class Document : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public DocumentKind Kind { get; set; }

    /// <summary>The version it is: "v4-backend".</summary>
    public required string Name { get; set; }

    public required string FileName { get; set; }

    public required string MediaType { get; set; }

    public long SizeBytes { get; set; }

    public required string StorageKey { get; set; }

    /// <summary>False until the browser's upload is confirmed in storage; only uploaded ones are listed.</summary>
    public bool Uploaded { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
