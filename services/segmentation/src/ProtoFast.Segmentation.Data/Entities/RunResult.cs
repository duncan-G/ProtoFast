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

    public required string TreeHash { get; set; }

    public DateTimeOffset FrozenAt { get; set; }

    public DateTimeOffset PublishedAt { get; set; }
}
