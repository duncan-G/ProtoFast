using System.Text.Json.Serialization;

namespace ProtoFast.Segmentation.Core.Tree;

/// <summary>
/// The structurer's raw return shape, exactly as <c>tree.schema.json</c> declares it
/// (Appendix B.2). It is deliberately a separate type from <c>SectionNode</c>: this one has no
/// section ids, may violate the children-xor-paragraphs rule, and may name paragraphs that do
/// not exist — all of which are things validation has to be able to <em>report</em>, so the
/// parse has to succeed first.
/// </summary>
public sealed record TreeProposal
{
    [JsonPropertyName("tree")]
    public TreeProposalNode? Tree { get; init; }

    [JsonPropertyName("paragraphEdits")]
    public IReadOnlyList<ParagraphEditProposal> ParagraphEdits { get; init; } = [];
}

public sealed record TreeProposalNode
{
    [JsonPropertyName("title")]
    public string Title { get; init; } = string.Empty;

    [JsonPropertyName("headingLineId")]
    public string? HeadingLineId { get; init; }

    [JsonPropertyName("level")]
    public int? Level { get; init; }

    [JsonPropertyName("inferred")]
    public bool Inferred { get; init; }

    [JsonPropertyName("children")]
    public IReadOnlyList<TreeProposalNode>? Children { get; init; }

    [JsonPropertyName("paragraphs")]
    public IReadOnlyList<string>? Paragraphs { get; init; }
}

public sealed record ParagraphEditProposal
{
    [JsonPropertyName("op")]
    public string Op { get; init; } = string.Empty;

    [JsonPropertyName("paragraphId")]
    public string ParagraphId { get; init; } = string.Empty;

    [JsonPropertyName("withParagraphId")]
    public string? WithParagraphId { get; init; }

    [JsonPropertyName("beforeSentence")]
    public int? BeforeSentence { get; init; }

    [JsonPropertyName("reason")]
    public string Reason { get; init; } = string.Empty;
}
