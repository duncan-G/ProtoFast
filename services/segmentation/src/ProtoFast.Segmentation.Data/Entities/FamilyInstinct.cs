namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// Learned per-family guidance (plan §15.4).
///
/// <para>Instincts are derived <em>only</em> from reviewer and human corrections, never from
/// document text — that restriction is what stops a document from teaching the pipeline to
/// misread the next one (plan §24.1). They are advisory: an instinct never overrides a trusted
/// boundary or a validation check, it only reaches a prompt.</para>
/// </summary>
public sealed class FamilyInstinct
{
    public long Id { get; set; }

    public required string Family { get; set; }

    /// <summary>The situation this applies to, in the words a prompt can use.</summary>
    public required string Pattern { get; set; }

    public required string Guidance { get; set; }

    public double Confidence { get; set; }

    public int Confirmations { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset LastSeen { get; set; }

    /// <summary>Set when a person promoted this instinct into a family skill; it stops being injected.</summary>
    public DateTimeOffset? PromotedAt { get; set; }
}
