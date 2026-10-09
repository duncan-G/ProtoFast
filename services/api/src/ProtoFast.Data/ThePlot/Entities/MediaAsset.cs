using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.ThePlot.Entities;

/// <summary>
/// A generated media file in the object store, content-addressed so the same request is never
/// rendered twice. Rows are fetched on their own (the sweeper, the cache), so they carry the owner.
/// </summary>
public sealed class MediaAsset : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid StoryId { get; set; }

    public Story Story { get; set; } = null!;

    public MediaAssetKind Kind { get; set; }

    /// <summary><c>stories/{storyId}/audio/{sha256}.mp3</c></summary>
    public required string StorageKey { get; set; }

    public required string ContentType { get; set; }

    /// <summary>SHA-256 of the bytes.</summary>
    public required string ContentHash { get; set; }

    public long ByteSize { get; set; }

    /// <summary>Hash of everything the request depended on; the cache key, unique per story and kind.</summary>
    public required string RequestHash { get; set; }

    public required string Provider { get; set; }

    public required string Model { get; set; }

    /// <summary>The provider's voice id the speech was made with.</summary>
    public string? Voice { get; set; }

    public int? DurationMs { get; set; }

    public long CostUsdMicros { get; set; }

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
