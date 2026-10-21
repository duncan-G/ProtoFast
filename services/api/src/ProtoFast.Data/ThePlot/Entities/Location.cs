using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

public sealed class Location : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public required string Name { get; set; }

    public LocationSetting Setting { get; set; }

    /// <summary>Colour-coding as an OKLCH hue, 0–359.</summary>
    public int Hue { get; set; }

    /// <summary>The location this one is within (HOSPITAL – TRAUMA WING), when a heading or beat says so.</summary>
    public Guid? InsideLocationId { get; set; }

    public Location? InsideLocation { get; set; }

    /// <summary>What the place is and how it looks as the story first finds it, inferred by the world model build.</summary>
    public string? Description { get; set; }

    /// <summary>What is built into it and dressed on it, part by part, as the story first finds it.</summary>
    public string? Contents { get; set; }

    public List<LocationPart> Parts { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
