using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Entities;

public sealed class Todo : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public required string Label { get; set; }

    public DateOnly? DueOn { get; set; }

    public DateTime? DoneAt { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
