using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Data.Entities;

/// <summary>
/// A pending human gate (plan §9.10).
///
/// <para>It is a database row as well as a workflow checkpoint on purpose: ThePlot lists
/// outstanding reviews for a signed-in reviewer, and doing that by inspecting workflow
/// checkpoints would mean the read path depends on the worker being up.</para>
/// </summary>
public sealed class ReviewTask
{
    public required string ReviewId { get; set; }

    public required string RunId { get; set; }

    public required string OwnerSubject { get; set; }

    public required string DocumentId { get; set; }

    public string DocumentFamily { get; set; } = Core.Ingest.FamilyDetector.Unknown;

    /// <summary>The workflow request id this decision must be posted back against.</summary>
    public string? WorkflowRequestId { get; set; }

    /// <summary>Reviewer findings as JSON, rendered inline on ThePlot's review screen.</summary>
    public string FindingsJson { get; set; } = "[]";

    /// <summary><c>pending | complete</c>.</summary>
    public string Status { get; set; } = "pending";

    public ReviewDecisionKind? Decision { get; set; }

    public string? Notes { get; set; }

    /// <summary>Keycloak <c>sub</c> of whoever decided, or null while pending.</summary>
    public string? DecidedBy { get; set; }

    /// <summary>Paragraph edits the reviewer applied, as JSON. Empty for a plain approval.</summary>
    public string EditsJson { get; set; } = "[]";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? DecidedAt { get; set; }
}
