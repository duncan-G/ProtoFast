using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A place the story can happen. Part of the story library; a scene heading points at one and
/// renders it as a slugline (<c>EXT. JUNKYARD — SCRAP HILL</c>).
/// </summary>
public sealed class Location : IDateStamped
{
    public Guid Id { get; set; }

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    /// <summary>Unique within the story; the editor offers "create" only when no name matches.</summary>
    public required string Name { get; set; }

    /// <summary>The slugline's <c>INT.</c> / <c>EXT.</c>.</summary>
    public LocationSetting Setting { get; set; }

    /// <summary>The colour the editor codes the location's blocks with, as an OKLCH hue in degrees (0–359).</summary>
    public int Hue { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
