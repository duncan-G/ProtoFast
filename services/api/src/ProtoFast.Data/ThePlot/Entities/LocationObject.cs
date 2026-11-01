using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>A thing the beats place in a location: doors, a gurney, blood on the floor, a crowd.</summary>
public sealed class LocationObject : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public required string Name { get; set; }

    /// <summary>Other names the beats use for it ("the doors", "the swing doors").</summary>
    public List<string> Aliases { get; set; } = [];

    public ObjectKind Kind { get; set; }

    /// <summary>Set when the object is one of the story's props.</summary>
    public Guid? PropId { get; set; }

    public Prop? Prop { get; set; }

    public string? Description { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
