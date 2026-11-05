using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Entities;

public sealed class Application : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    public Guid CompanyId { get; set; }

    public Company Company { get; set; } = null!;

    public string Role { get; set; } = "";

    /// <summary>The furthest it got; a closed application keeps the stage it closed at.</summary>
    public ApplicationStage Stage { get; set; }

    public bool Closed { get; set; }

    /// <summary>Null until you apply.</summary>
    public DateOnly? AppliedOn { get; set; }

    public ApplicationSource? Source { get; set; }

    /// <summary>Who referred you or which board: "Sam Okafor", "LinkedIn".</summary>
    public string SourceDetail { get; set; } = "";

    /// <summary>The resume version sent: "v4-backend". Kept from before files; a file's name supersedes it.</summary>
    public string Resume { get; set; } = "";

    public Guid? ResumeId { get; set; }

    public Guid? CvId { get; set; }

    public string PostingUrl { get; set; } = "";

    /// <summary>The posting's text, kept in case it comes down.</summary>
    public string Posting { get; set; } = "";

    public string NextStep { get; set; } = "";

    public DateOnly? NextOn { get; set; }

    public string PostedPay { get; set; } = "";

    public string AskedPay { get; set; } = "";

    public string OfferedPay { get; set; } = "";

    /// <summary>Equity, bonus, vesting, sign-on.</summary>
    public string OfferTerms { get; set; } = "";

    public List<string> Wants { get; set; } = [];

    /// <summary>Questions still to ask them.</summary>
    public List<string> Questions { get; set; } = [];

    /// <summary>Questions put to them, moved here from <see cref="Questions"/> once asked.</summary>
    public List<AskedQuestion> Asked { get; set; } = [];

    public string Team { get; set; } = "";

    public string ReportsTo { get; set; } = "";

    public List<string> Stack { get; set; } = [];

    public WorkArrangement? Arrangement { get; set; }

    public string Location { get; set; } = "";

    /// <summary>Timezone or overlap the team expects: "4h overlap with ET".</summary>
    public string Hours { get; set; } = "";

    /// <summary>Null when not known.</summary>
    public bool? SponsorsVisa { get; set; }

    public List<string> Benefits { get; set; } = [];

    public List<Round> Rounds { get; set; } = [];

    /// <summary>Oldest first.</summary>
    public List<Learning> Learnings { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
