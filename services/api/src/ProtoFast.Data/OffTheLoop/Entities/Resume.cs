using ProtoFast.Database.Abstractions;

namespace ProtoFast.Data.OffTheLoop.Entities;

/// <summary>
/// The general resume (no application) holds everything that could go on a resume; one tailored to
/// an application is what goes to that employer. Months are written "2023-04", or "2023" alone.
/// </summary>
public sealed class Resume : IDateStamped
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = "";

    /// <summary>Null for the general resume.</summary>
    public Guid? ApplicationId { get; set; }

    public string Name { get; set; } = "";

    /// <summary>One line under the name: "Senior backend engineer · payments".</summary>
    public string Headline { get; set; } = "";

    public string Email { get; set; } = "";

    public string Phone { get; set; } = "";

    public string Location { get; set; } = "";

    public List<ResumeLink> Links { get; set; } = [];

    public string Summary { get; set; } = "";

    public List<ResumeRole> Experience { get; set; } = [];

    public List<ResumeProject> Projects { get; set; } = [];

    public List<ResumeGroup> Skills { get; set; } = [];

    public List<ResumeSchool> Education { get; set; } = [];

    /// <summary>Certifications, awards, talks, open source and the like.</summary>
    public List<ResumeGroup> Extras { get; set; } = [];

    /// <summary>A tailored resume's: what it leans on, and what the job asks that it can't show.</summary>
    public List<string> Notes { get; set; } = [];

    public DateTime DateCreated { get; set; }

    public DateTime DateLastModified { get; set; }
}
