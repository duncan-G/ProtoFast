using System.IO.Compression;
using System.Security;
using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop;
using Xunit;
using DocumentKindRecord = ProtoFast.Data.OffTheLoop.Entities.DocumentKind;
using Loop = ProtoFast.Api.OffTheLoop;

namespace ProtoFast.Api.IntegrationTests;

public class ResumeTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);
    private readonly Seeker _stranger = new(database);

    private static readonly object[] None = [];

    private static Resume General()
    {
        var resume = new Resume { Name = "Ada Park", Email = "ada@example.com", Headline = "Backend engineer", Summary = "Builds payment systems." };
        resume.Links.Add(new ResumeLink { Label = "GitHub", Url = "https://github.com/ada" });
        var role = new ResumeRole { Company = "Halcyon", Title = "Senior engineer", Start = "2021-03" };
        role.Highlights.Add("Moved billing onto a queue");
        role.Stack.Add("Go");
        role.Evidence.Add(new ResumeEvidence { Repo = "billing", Commit = "A1B2C3D", Date = "2025-03-14", What = "Split the nightly run" });
        resume.Experience.Add(role);
        var languages = new ResumeGroup { Name = "languages" };
        languages.Items.Add("Go");
        resume.Skills.Add(languages);
        return resume;
    }

    private static RunWork Read(Loop.Document file) => new() { ReadResume = new ReadResumeRequest { DocumentId = file.Id } };

    private static RunWork Tailor(Application application, string instructions = "") =>
        new() { TailorResume = new TailorResumeRequest { ApplicationId = application.Id, Instructions = instructions } };

    private static byte[] Docx(params string[] paragraphs)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var writer = new StreamWriter(zip.CreateEntry("word/document.xml").Open());
            var body = string.Concat(paragraphs.Select(p =>
                $"<w:p><w:r><w:t>{SecurityElement.Escape(p).Replace("\t", "</w:t><w:tab/><w:t>")}</w:t></w:r></w:p>"));
            writer.Write($"<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body>{body}</w:body></w:document>");
        }

        return buffer.ToArray();
    }

    private static object ReadAnswer(string outcome = "read", string name = "Ada Park") => new
    {
        outcome,
        name,
        headline = "",
        email = "",
        phone = "",
        location = "",
        links = None,
        summary = "",
        experience = None,
        projects = None,
        skills = None,
        education = None,
        extras = None,
    };

    [Fact]
    public async Task The_general_resume_is_made_on_its_first_save_then_replaced()
    {
        var first = await _seeker.SaveResumeAsync(General());

        Assert.Equal("", first.ApplicationId);
        var evidence = Assert.Single(Assert.Single(first.Experience).Evidence);
        Assert.Equal(("a1b2c3d", "2025-03-14"), (evidence.Commit, evidence.Date));

        var changed = General();
        changed.Headline = "Staff engineer";
        changed.Experience.Clear();
        var second = await _seeker.SaveResumeAsync(changed);

        Assert.Equal(first.Id, second.Id);
        var listed = Assert.Single(await _seeker.ListResumesAsync());
        Assert.Equal("Staff engineer", listed.Headline);
        Assert.Empty(listed.Experience);
        Assert.Equal(["Go"], Assert.Single(listed.Skills).Items);
        Assert.Empty(await _stranger.ListResumesAsync());
    }

    [Fact]
    public async Task A_tailored_resume_belongs_to_one_of_your_applications_and_goes_with_it()
    {
        var company = await _seeker.CreateCompanyAsync("Linear");
        var application = await _seeker.CreateApplicationAsync(Seeker.NewApplication(company.Id));
        await _seeker.SaveResumeAsync(General());
        var tailored = General();
        tailored.ApplicationId = application.Id;
        tailored.Notes.Add("Led with billing");

        var saved = await _seeker.SaveResumeAsync(tailored);

        Assert.Equal(application.Id, saved.ApplicationId);
        Assert.Equal(["Led with billing"], saved.Notes);
        Assert.Equal(["", application.Id], (await _seeker.ListResumesAsync()).Select(r => r.ApplicationId));

        var theirs = General();
        theirs.ApplicationId = application.Id;
        var refused = await Assert.ThrowsAsync<RpcException>(() => _stranger.SaveResumeAsync(theirs));
        Assert.Equal(StatusCode.InvalidArgument, refused.StatusCode);

        await _seeker.Applications((s, c) => s.DeleteApplication(new DeleteApplicationRequest { ApplicationId = application.Id }, c));
        Assert.Equal([""], (await _seeker.ListResumesAsync()).Select(r => r.ApplicationId));
    }

    [Theory]
    [InlineData("month", "When a job started isn’t a month, written like 2023-04.")]
    [InlineData("company", "Give each job its company.")]
    [InlineData("link", "A link isn’t a web address.")]
    [InlineData("commit", "Each commit needs its hash.")]
    public async Task Saving_checks_what_it_holds(string wrong, string message)
    {
        var resume = General();
        switch (wrong)
        {
            case "month":
                resume.Experience[0].Start = "March 2021";
                break;
            case "company":
                resume.Experience[0].Company = " ";
                break;
            case "link":
                resume.Links[0].Url = "github.com/ada";
                break;
            case "commit":
                resume.Experience[0].Evidence[0].Commit = "HEAD";
                break;
        }

        var refused = await Assert.ThrowsAsync<RpcException>(() => _seeker.SaveResumeAsync(resume));

        Assert.Equal(StatusCode.InvalidArgument, refused.StatusCode);
        Assert.Equal(message, refused.Status.Detail);
    }

    [Fact]
    public async Task Only_your_own_resume_can_be_deleted()
    {
        var saved = await _seeker.SaveResumeAsync(General());

        var refused = await Assert.ThrowsAsync<RpcException>(() =>
            _stranger.Resumes((s, c) => s.DeleteResume(new DeleteResumeRequest { ResumeId = saved.Id }, c)));
        Assert.Equal(StatusCode.NotFound, refused.StatusCode);

        await _seeker.Resumes((s, c) => s.DeleteResume(new DeleteResumeRequest { ResumeId = saved.Id }, c));
        Assert.Empty(await _seeker.ListResumesAsync());
    }

    [Fact]
    public async Task Reading_a_word_resume_sends_its_text_and_tidies_what_comes_back()
    {
        var file = await _seeker.UploadAsync(
            DocumentKindRecord.Resume, "ada-2025.docx", Docx("Ada Park", "Halcyon\tSenior engineer", "• Moved billing onto a queue"));
        var model = new ScriptedResearchModel(
            new
            {
                outcome = "read",
                name = " Ada Park ",
                headline = "",
                email = "ada@example.com",
                phone = "",
                location = "Oakland, CA",
                links = new[] { new { label = "GitHub", url = "https://github.com/ada" }, new { label = "site", url = "ada.dev" } },
                summary = "",
                experience = new object[]
                {
                    new
                    {
                        company = "Halcyon", title = "Senior engineer", location = "", start = "Mar 2021", end = "Present", summary = "",
                        highlights = new[] { "Moved billing onto a queue", "Moved billing onto a queue" }, stack = new[] { "Go" },
                    },
                    new { company = "", title = "Intern", location = "", start = "2019", end = "2019", summary = "", highlights = None, stack = None },
                },
                projects = None,
                skills = new[] { new { name = "languages", items = new[] { "Go", "SQL" } } },
                education = None,
                extras = None,
            },
            (ResearchAction.Thinking, "Reading the jobs"));
        _seeker.Script(model);

        var run = await _seeker.StartRunAsync(Read(file), "Read ada-2025.docx");
        Assert.Equal(RunKind.ReadResume, run.Kind);
        Assert.True(await _seeker.RunNextAsync());

        var done = await _seeker.GetRunAsync(run.Id);
        Assert.True(done.Run.Status == RunStatus.Succeeded, done.Run.Error);
        var read = done.Result.Resume.Resume;
        Assert.Equal("Ada Park", read.Name);
        Assert.Equal(["https://github.com/ada"], read.Links.Select(l => l.Url));
        var role = Assert.Single(read.Experience);
        Assert.Equal(("2021-03", ""), (role.Start, role.End));
        Assert.Equal(["Moved billing onto a queue"], role.Highlights);
        Assert.Equal(["Reading the jobs"], done.Progress.Steps.Select(s => s.Subject));
        Assert.Contains("Ada Park\nHalcyon\tSenior engineer\n• Moved billing onto a queue", model.Brief!.User);
        Assert.Null(model.Brief.File);

        await _seeker.SaveResumeAsync(read);
    }

    [Fact]
    public async Task A_pdf_goes_to_the_model_as_it_is()
    {
        var pdf = "%PDF-1.7 a resume"u8.ToArray();
        var file = await _seeker.UploadAsync(DocumentKindRecord.Resume, "ada.pdf", pdf);
        var model = new ScriptedResearchModel(ReadAnswer());
        _seeker.Script(model);

        var run = await _seeker.StartRunAsync(Read(file));
        await _seeker.RunNextAsync();

        Assert.Equal(RunStatus.Succeeded, (await _seeker.GetRunAsync(run.Id)).Run.Status);
        Assert.Equal("application/pdf", model.Brief!.File!.MediaType);
        Assert.Equal(pdf, model.Brief.File.Data);
        Assert.Contains("ada.pdf", model.Brief.User);
    }

    [Theory]
    [InlineData("old.doc", "doc", LoopErrors.ResumeFileFormat)]
    [InlineData("broken.docx", "junk", LoopErrors.ResumeUnreadable)]
    [InlineData("list.docx", "shopping", LoopErrors.ResumeNotAResume)]
    public async Task A_file_that_isnt_a_readable_resume_fails_its_run_saying_why(string fileName, string content, string error)
    {
        var bytes = content == "shopping" ? Docx("Milk", "Eggs") : "not a zip at all"u8.ToArray();
        var file = await _seeker.UploadAsync(DocumentKindRecord.Resume, fileName, bytes);
        _seeker.Script(new ScriptedResearchModel(ReadAnswer("not_a_resume", "")));

        var run = await _seeker.StartRunAsync(Read(file));
        await _seeker.RunNextAsync();

        var failed = await _seeker.GetRunAsync(run.Id);
        Assert.Equal(RunStatus.Failed, failed.Run.Status);
        Assert.Equal(error, failed.Run.Error);
    }

    [Fact]
    public async Task Tailoring_needs_your_application_and_a_resume_to_tailor()
    {
        var company = await _seeker.CreateCompanyAsync("Linear");
        var application = await _seeker.CreateApplicationAsync(Seeker.NewApplication(company.Id));

        var empty = await Assert.ThrowsAsync<RpcException>(() => _seeker.StartRunAsync(Tailor(application)));
        Assert.Equal(StatusCode.FailedPrecondition, empty.StatusCode);
        Assert.Equal(LoopErrors.ResumeEmpty, empty.Status.Detail);

        await _stranger.SaveResumeAsync(General());
        var theirs = await Assert.ThrowsAsync<RpcException>(() => _stranger.StartRunAsync(Tailor(application)));
        Assert.Equal(StatusCode.NotFound, theirs.StatusCode);
    }

    [Fact]
    public async Task Tailoring_reads_the_job_the_resume_and_the_stories_and_keeps_how_to_reach_you()
    {
        var company = await _seeker.CreateCompanyAsync("Linear");
        company.Values.Add("Craft");
        await _seeker.UpdateCompanyAsync(company);
        var draft = Seeker.NewApplication(company.Id);
        draft.Role = "Senior SWE";
        draft.Posting = "We need Go and queues.";
        draft.Wants.Add("5+ years backend");
        var application = await _seeker.CreateApplicationAsync(draft);
        await _seeker.SaveResumeAsync(General());
        await _seeker.ImportStoriesAsync(new Note
        {
            Kind = NoteKind.Story, Title = "Billing <queue>", Tag = "ownership", Body = "Situation: nightly runs timed out.",
        });
        await _seeker.CreateNoteAsync(new Note { Kind = NoteKind.Pattern, Title = "Monotonic stack", Body = "## Stack" });
        var model = new ScriptedResearchModel(new
        {
            name = "Someone Else",
            headline = "Senior backend engineer",
            email = "",
            phone = "",
            location = "",
            links = None,
            summary = "Builds queues.",
            experience = new[]
            {
                new
                {
                    company = "Halcyon", title = "Senior engineer", location = "", start = "2021-03", end = "", summary = "",
                    highlights = new[] { "Moved billing onto a queue, cutting runs from 6 h to 20 min" }, stack = new[] { "Go" },
                },
            },
            projects = None,
            skills = None,
            education = None,
            extras = None,
            notes = new[] { "Led with the billing queue: the posting asks for queues.", "No Kubernetes in your history." },
        });
        _seeker.Script(model);

        var run = await _seeker.StartRunAsync(Tailor(application, "Lean on payments"));
        Assert.Equal((RunKind.TailorResume, application.Id, company.Id), (run.Kind, run.ApplicationId, run.CompanyId));
        await _seeker.RunNextAsync();

        var done = await _seeker.GetRunAsync(run.Id);
        Assert.True(done.Run.Status == RunStatus.Succeeded, done.Run.Error);
        var tailored = done.Result.Resume.Resume;
        Assert.Equal(("Ada Park", "ada@example.com"), (tailored.Name, tailored.Email));
        Assert.Equal(["https://github.com/ada"], tailored.Links.Select(l => l.Url));
        Assert.Equal("Senior backend engineer", tailored.Headline);
        Assert.Equal(2, tailored.Notes.Count);
        var user = model.Brief!.User;
        Assert.Contains("company: Linear", user);
        Assert.Contains("values: Craft", user);
        Assert.Contains("they want: 5+ years backend", user);
        Assert.Contains("<posting>\nWe need Go and queues.\n</posting>", user);
        Assert.Contains("Moved billing onto a queue", user);
        Assert.DoesNotContain("a1b2c3d", user);
        Assert.Contains("<story title=\"Billing &lt;queue&gt;\" quality=\"ownership\">\nSituation: nightly runs timed out.\n</story>", user);
        Assert.DoesNotContain("Monotonic stack", user);
        Assert.Contains("<instructions>\nLean on payments\n</instructions>", user);

        tailored.ApplicationId = application.Id;
        Assert.Equal(application.Id, (await _seeker.SaveResumeAsync(tailored)).ApplicationId);
    }
}
