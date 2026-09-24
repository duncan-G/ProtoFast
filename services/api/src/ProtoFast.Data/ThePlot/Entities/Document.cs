using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A document a ThePlot user has brought into the platform. Lives in the <c>plot</c> schema of
/// the <c>protofast</c> database (<see cref="ThePlotDbContext"/>).
/// </summary>
public sealed class Document : IDateStamped
{
    public Guid Id { get; set; }

    /// <summary>
    /// The owner's subject from the internal JWT. Stamped from the caller on insert and never
    /// taken from a request; reads are filtered to it and writes for another owner are refused.
    /// </summary>
    public string UserId { get; set; } = "";

    public required string Name { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
