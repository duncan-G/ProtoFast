using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Entities;

public sealed class Goal : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    /// <summary>What it measures: "salary", "title".</summary>
    public required string Kind { get; set; }

    public required string Target { get; set; }

    public required string Current { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
