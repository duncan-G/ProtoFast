using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A picture of one of a scene's views: the place its camera sees from where it stands, with no
/// one in it. Parts are kept by name, not id, because a world model build makes its parts anew.
/// </summary>
public sealed class ViewPicture : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public Guid SceneId { get; set; }

    public Scene Scene { get; set; } = null!;

    public Guid CameraLocationId { get; set; }

    public Location CameraLocation { get; set; } = null!;

    /// <summary>The part's name, after its parents' ("hallway / end"); null for the whole location.</summary>
    public string? CameraPart { get; set; }

    /// <summary>Null when the camera looks toward nothing in particular.</summary>
    public Guid? TowardLocationId { get; set; }

    public Location? TowardLocation { get; set; }

    public string? TowardPart { get; set; }

    /// <summary>How many physical changes to the place came between this camera's earlier beats in the scene and the view's.</summary>
    public int State { get; set; }

    /// <summary><c>stories/{storyId}/views/{sha256}.{ext}</c></summary>
    public required string StorageKey { get; set; }

    public required string ContentType { get; set; }

    public long ByteSize { get; set; }

    /// <summary>What the image model was asked, the reference pictures' captions included.</summary>
    public required string Prompt { get; set; }

    public string? Notes { get; set; }

    public long CostUsdMicros { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
