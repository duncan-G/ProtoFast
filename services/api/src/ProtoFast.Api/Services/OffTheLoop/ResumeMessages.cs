using System.Text.RegularExpressions;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop;
using EvidenceRecord = ProtoFast.Data.OffTheLoop.Entities.ResumeEvidence;
using GroupRecord = ProtoFast.Data.OffTheLoop.Entities.ResumeGroup;
using LinkRecord = ProtoFast.Data.OffTheLoop.Entities.ResumeLink;
using ProjectRecord = ProtoFast.Data.OffTheLoop.Entities.ResumeProject;
using ResumeRecord = ProtoFast.Data.OffTheLoop.Entities.Resume;
using RoleRecord = ProtoFast.Data.OffTheLoop.Entities.ResumeRole;
using SchoolRecord = ProtoFast.Data.OffTheLoop.Entities.ResumeSchool;

namespace ProtoFast.Api.Services.OffTheLoop;

public static partial class ResumeMessages
{
    public static Resume ToMessage(ResumeRecord resume)
    {
        var message = new Resume
        {
            Id = resume.Id.ToString(),
            ApplicationId = resume.ApplicationId?.ToString() ?? "",
            Name = resume.Name,
            Headline = resume.Headline,
            Email = resume.Email,
            Phone = resume.Phone,
            Location = resume.Location,
            Summary = resume.Summary,
            LastModifiedUnixSeconds = LoopText.UnixSeconds(resume.DateLastModified),
        };
        message.Links.AddRange(resume.Links.Select(l => new ResumeLink { Label = l.Label, Url = l.Url }));
        message.Experience.AddRange(resume.Experience.Select(ToMessage));
        message.Projects.AddRange(resume.Projects.Select(ToMessage));
        message.Skills.AddRange(resume.Skills.Select(ToMessage));
        message.Education.AddRange(resume.Education.Select(ToMessage));
        message.Extras.AddRange(resume.Extras.Select(ToMessage));
        message.Notes.AddRange(resume.Notes);
        return message;
    }

    /// <summary>The application is checked by the caller, which can see whose it is.</summary>
    public static ResumeRecord ToRecord(Resume message) => new()
    {
        ApplicationId = message.ApplicationId.Length == 0
            ? null
            : Guid.TryParse(message.ApplicationId, out var applicationId)
                ? applicationId
                : throw LoopErrors.Invalid(LoopErrors.ApplicationNotFound),
        Name = LoopText.Optional(message.Name, OffTheLoopLimits.Name, "Your name"),
        Headline = LoopText.Optional(message.Headline, OffTheLoopLimits.Line, "The headline"),
        Email = LoopText.Optional(message.Email, OffTheLoopLimits.Name, "The email"),
        Phone = LoopText.Optional(message.Phone, OffTheLoopLimits.Label, "The phone number"),
        Location = LoopText.Optional(message.Location, OffTheLoopLimits.Label, "Where you are"),
        Links = Entries(message.Links, l => new LinkRecord
        {
            Label = LoopText.Optional(l.Label, OffTheLoopLimits.Label, "A link’s label"),
            Url = Required(LoopText.Url(l.Url, OffTheLoopLimits.Url, "A link"), "Give each link its address."),
        }, OffTheLoopLimits.ListItems, $"A resume can list at most {OffTheLoopLimits.ListItems} links."),
        Summary = LoopText.Optional(message.Summary, OffTheLoopLimits.Text, "The summary"),
        Experience = Entries(message.Experience, ToRecord, OffTheLoopLimits.ResumeEntries, TooMany("jobs")),
        Projects = Entries(message.Projects, ToRecord, OffTheLoopLimits.ResumeEntries, TooMany("projects")),
        Skills = Entries(
            message.Skills, g => ToRecord(g, OffTheLoopLimits.Label, "A skill"), OffTheLoopLimits.ListItems,
            $"A resume can hold at most {OffTheLoopLimits.ListItems} skill groups."),
        Education = Entries(message.Education, ToRecord, OffTheLoopLimits.ResumeEntries, TooMany("schools")),
        Extras = Entries(
            message.Extras, g => ToRecord(g, OffTheLoopLimits.Line, "An entry"), OffTheLoopLimits.ListItems,
            $"A resume can hold at most {OffTheLoopLimits.ListItems} other lists."),
        Notes = LoopText.Items(message.Notes, OffTheLoopLimits.Line, "The notes"),
    };

    public static void Overwrite(ResumeRecord target, ResumeRecord source)
    {
        target.Name = source.Name;
        target.Headline = source.Headline;
        target.Email = source.Email;
        target.Phone = source.Phone;
        target.Location = source.Location;
        target.Links = source.Links;
        target.Summary = source.Summary;
        target.Experience = source.Experience;
        target.Projects = source.Projects;
        target.Skills = source.Skills;
        target.Education = source.Education;
        target.Extras = source.Extras;
        target.Notes = source.Notes;
    }

    /// <summary>Nothing on it yet, so there is nothing to tailor.</summary>
    public static bool IsEmpty(ResumeRecord resume) =>
        resume.Summary.Length == 0 && resume.Experience.Count == 0 && resume.Projects.Count == 0
        && resume.Skills.Count == 0 && resume.Education.Count == 0 && resume.Extras.Count == 0;

    private static ResumeRole ToMessage(RoleRecord role)
    {
        var message = new ResumeRole
        {
            Company = role.Company,
            Title = role.Title,
            Location = role.Location,
            Start = role.Start,
            End = role.End,
            Summary = role.Summary,
        };
        message.Highlights.AddRange(role.Highlights);
        message.Stack.AddRange(role.Stack);
        message.Evidence.AddRange(role.Evidence.Select(ToMessage));
        return message;
    }

    private static ResumeProject ToMessage(ProjectRecord project)
    {
        var message = new ResumeProject
        {
            Name = project.Name,
            Link = project.Link,
            Start = project.Start,
            End = project.End,
            Summary = project.Summary,
        };
        message.Highlights.AddRange(project.Highlights);
        message.Stack.AddRange(project.Stack);
        message.Evidence.AddRange(project.Evidence.Select(ToMessage));
        return message;
    }

    private static ResumeSchool ToMessage(SchoolRecord school)
    {
        var message = new ResumeSchool { School = school.School, Degree = school.Degree, Start = school.Start, End = school.End };
        message.Details.AddRange(school.Details);
        return message;
    }

    private static ResumeGroup ToMessage(GroupRecord group)
    {
        var message = new ResumeGroup { Name = group.Name };
        message.Items.AddRange(group.Items);
        return message;
    }

    private static ResumeEvidence ToMessage(EvidenceRecord evidence) => new()
    {
        Repo = evidence.Repo,
        Commit = evidence.Commit,
        Date = evidence.Date is { } date ? LoopText.Format(date) : "",
        What = evidence.What,
    };

    private static RoleRecord ToRecord(ResumeRole message) => new()
    {
        Company = LoopText.Required(message.Company, OffTheLoopLimits.Name, "A job’s company", "Give each job its company."),
        Title = LoopText.Optional(message.Title, OffTheLoopLimits.Name, "A job title"),
        Location = LoopText.Optional(message.Location, OffTheLoopLimits.Label, "Where a job was"),
        Start = LoopText.Month(message.Start, "When a job started"),
        End = LoopText.Month(message.End, "When a job ended"),
        Summary = LoopText.Optional(message.Summary, OffTheLoopLimits.Summary, "A job’s summary"),
        Highlights = LoopText.Items(message.Highlights, OffTheLoopLimits.Line, "A job’s highlights"),
        Stack = LoopText.Items(message.Stack, OffTheLoopLimits.Label, "A job’s stack"),
        Evidence = Evidence(message.Evidence),
    };

    private static ProjectRecord ToRecord(ResumeProject message) => new()
    {
        Name = LoopText.Required(message.Name, OffTheLoopLimits.Name, "A project’s name", "Give each project a name."),
        Link = LoopText.Url(message.Link, OffTheLoopLimits.Url, "A project’s link"),
        Start = LoopText.Month(message.Start, "When a project started"),
        End = LoopText.Month(message.End, "When a project ended"),
        Summary = LoopText.Optional(message.Summary, OffTheLoopLimits.Summary, "A project’s summary"),
        Highlights = LoopText.Items(message.Highlights, OffTheLoopLimits.Line, "A project’s highlights"),
        Stack = LoopText.Items(message.Stack, OffTheLoopLimits.Label, "A project’s stack"),
        Evidence = Evidence(message.Evidence),
    };

    private static SchoolRecord ToRecord(ResumeSchool message) => new()
    {
        School = LoopText.Required(message.School, OffTheLoopLimits.Name, "A school’s name", "Give each school its name."),
        Degree = LoopText.Optional(message.Degree, OffTheLoopLimits.Name, "A degree"),
        Start = LoopText.Month(message.Start, "When school started"),
        End = LoopText.Month(message.End, "When school ended"),
        Details = LoopText.Items(message.Details, OffTheLoopLimits.Line, "A school’s details"),
    };

    private static GroupRecord ToRecord(ResumeGroup message, int max, string item) => new()
    {
        Name = LoopText.Required(message.Name, OffTheLoopLimits.Label, "A list’s name", "Give each list a name."),
        Items = LoopText.Items(message.Items, max, item),
    };

    private static List<EvidenceRecord> Evidence(IEnumerable<ResumeEvidence> evidence) =>
        Entries(evidence, e => new EvidenceRecord
        {
            Repo = LoopText.Optional(e.Repo, OffTheLoopLimits.Name, "A commit’s repository"),
            Commit = CommitHash().IsMatch(e.Commit.Trim())
                ? e.Commit.Trim().ToLowerInvariant()
                : throw LoopErrors.Invalid("Each commit needs its hash."),
            Date = LoopText.OptionalDay(e.Date, "A commit’s date"),
            What = LoopText.Optional(e.What, OffTheLoopLimits.Line, "What a commit shows"),
        }, OffTheLoopLimits.ListItems, $"An entry can cite at most {OffTheLoopLimits.ListItems} commits.");

    private static List<T> Entries<TMessage, T>(IEnumerable<TMessage> messages, Func<TMessage, T> toRecord, int max, string tooMany)
    {
        var entries = messages.Select(toRecord).ToList();
        return entries.Count <= max ? entries : throw LoopErrors.Invalid(tooMany);
    }

    private static string TooMany(string noun) => $"A resume can hold at most {OffTheLoopLimits.ResumeEntries} {noun}.";

    private static string Required(string text, string missing) =>
        text.Length > 0 ? text : throw LoopErrors.Invalid(missing);

    [GeneratedRegex("^[0-9a-fA-F]{7,40}$")]
    private static partial Regex CommitHash();
}
