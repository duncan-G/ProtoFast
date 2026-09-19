namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// The published result (plan §9.13): the frozen tree, paragraphs and augmentations as JSON, so
/// <c>GetResult</c> is one indexed read and never touches S3.
///
/// <para>The artifacts in S3 remain the record of how the run got here; this row is the product.
/// Keeping them separate is what lets run artifacts expire after 30 days while the result stays.</para>
/// </summary>
public sealed class RunResult
{
    public required string RunId { get; set; }

    public required string OwnerSubject { get; set; }

    public required string DocumentId { get; set; }

    public required string TreeJson { get; set; }

    public required string ParagraphsJson { get; set; }

    public string AugmentationsJson { get; set; } = "[]";

    /// <summary>
    /// The scene layers, published beside the tree so <c>GetScenes</c> is the same single indexed
    /// read as <c>GetResult</c> (scene plan §10, milestone S7).
    ///
    /// <para>Defaulted rather than required because a run published before the scene phases
    /// existed — or one whose document yielded no displayable text — has no scenes, and that is a
    /// fact about the document rather than a broken row. A reader distinguishes the two by whether
    /// the run reached phase 10, not by whether this column is empty.</para>
    /// </summary>
    public string ScenesJson { get; set; } = "[]";

    /// <summary>
    /// The items the scenes name. Stored separately rather than nested inside each scene because a
    /// scene names its items by id (C9) and the same item list is what <c>GetScenes</c> resolves
    /// span text from — duplicating it per scene would make the row grow with the nesting rather
    /// than with the document.
    /// </summary>
    public string ItemsJson { get; set; } = "[]";

    /// <summary>The persona, place and exhibit tables a tag or a cast entry resolves against.</summary>
    public string RegistriesJson { get; set; } = "{}";

    public required string TreeHash { get; set; }

    public DateTimeOffset FrozenAt { get; set; }

    public DateTimeOffset PublishedAt { get; set; }
}
