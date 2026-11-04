using System.Text.Json;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Api.Services.OffTheLoop;
using Xunit;

namespace ProtoFast.Api.IntegrationTests;

public class ResearcherTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);
    private readonly Seeker _stranger = new(database);
    private readonly PostedPages _pages = database.Services.GetRequiredService<PostedPages>();

    private const string Duties =
        "You'll own the ledger: design, build and run the services that move money between accounts, carry "
        + "their on-call one week in six, and work with product on what ships next. The team is six engineers.";

    [Fact]
    public async Task Company_research_streams_its_steps_then_what_it_found()
    {
        var company = await _seeker.CreateCompanyAsync("Halcyon <Pay>");
        company.Revenue = "$140M";
        company.Location = "Oakland, CA";
        company.Values.Add("Ownership");
        company.Contacts.Add(new Contact { Name = "Lin Ho", Role = "recruiter" });
        await _seeker.UpdateCompanyAsync(company);
        var model = new ScriptedResearchModel(
            new
            {
                about = "  fintech · payments api  ",
                location = "San Francisco, CA",
                revenue = "",
                headcount = new string('9', 200),
                values = new[] { "Default to writing", "default to writing", " " },
                competitors = new[] { "Stripe", "Adyen" },
                executives = new[]
                {
                    new { name = " Ada Park ", role = "CEO · co-founder" },
                    new { name = "ada park", role = "CEO" },
                    new { name = "", role = "CTO" },
                    new { name = "Sam Ortiz", role = "" },
                },
            },
            (ResearchAction.Thinking, "Checking the funding"),
            (ResearchAction.Searching, "halcyon pay funding"),
            (ResearchAction.Reading, "https://news.example/halcyon"))
        {
            Sources = [new WebSource { Title = "news.example", Url = "https://news.example/halcyon" }],
        };
        var replies = new RecordingStreamWriter<ResearchCompanyReply>();

        await _seeker.Research(model, (s, c) =>
            s.ResearchCompany(new ResearchCompanyRequest { CompanyId = company.Id }, replies, c));

        Assert.Equal(
            ["Checking the funding", "halcyon pay funding", "https://news.example/halcyon"],
            replies.Messages.Where(r => r.Step is not null).Select(r => r.Step.Subject));
        var findings = Assert.Single(replies.Messages, r => r.Findings is not null).Findings;
        Assert.Equal("fintech · payments api", findings.Company.About);
        Assert.Equal("San Francisco, CA", findings.Company.Location);
        Assert.Equal("", findings.Company.Revenue);
        Assert.Equal(80, findings.Company.Headcount.Length);
        Assert.Equal(["Default to writing"], findings.Company.Values);
        Assert.Equal(["Stripe", "Adyen"], findings.Company.Competitors);
        Assert.Equal(
            [("Ada Park", "CEO · co-founder"), ("Sam Ortiz", "")],
            findings.Company.Contacts.Select(c => (c.Name, c.Role)));
        Assert.All(findings.Company.Contacts, c => Assert.False(c.HasMetOn));
        Assert.Equal(["https://news.example/halcyon"], findings.Sources.Select(s => s.Url));
        Assert.Contains("<company>Halcyon &lt;Pay&gt;</company>", model.Brief!.User);
        Assert.Contains("revenue: $140M", model.Brief.User);
        Assert.Contains("location: Oakland, CA", model.Brief.User);
        Assert.Contains("values: Ownership", model.Brief.User);
        Assert.Contains("people: Lin Ho (recruiter)", model.Brief.User);
        Assert.True(model.Brief.Web);
    }

    [Fact]
    public async Task Another_users_company_is_not_researched()
    {
        var company = await _seeker.CreateCompanyAsync("Corvid Games");
        var model = new ScriptedResearchModel(new { });

        var error = await Assert.ThrowsAsync<RpcException>(() => _stranger.Research(model, (s, c) =>
            s.ResearchCompany(
                new ResearchCompanyRequest { CompanyId = company.Id }, new RecordingStreamWriter<ResearchCompanyReply>(), c)));

        Assert.Equal(StatusCode.NotFound, error.StatusCode);
        Assert.Null(model.Brief);
    }

    [Fact]
    public async Task A_posting_link_is_fetched_and_read_into_an_application()
    {
        var company = await _seeker.CreateCompanyAsync("Northwind");
        _pages.Post("https://www.jobs.example/4021", Declaring("Senior SWE", Duties));
        var model = new ScriptedResearchModel(
            new
            {
                outcome = "read",
                employer = "Northwind Labs",
                role = "Senior SWE, Payments",
                page = "https://www.jobs.example/4021",
                summary = "A payments role.",
                posted_pay = "$180–220k",
                arrangement = "hybrid",
                sponsors_visa = "no",
                stack = new[] { "Go", "Postgres" },
                questions = new[] { "Who owns on-call?" },
                about = "fintech · ledgers",
            },
            (ResearchAction.Reading, "https://www.jobs.example/4021"))
        {
            Sources =
            [
                new WebSource { Title = "jobs.example", Url = "https://www.jobs.example/4021" },
                new WebSource { Title = "board.example", Url = "https://board.example/northwind" },
            ],
        };
        var replies = new RecordingStreamWriter<ReadPostingReply>();

        await _seeker.Research(model, (s, c) => s.ReadPosting(
            new ReadPostingRequest { Url = "https://www.jobs.example/4021", CompanyId = company.Id }, replies, c));

        var findings = replies.Messages.Last().Findings;
        Assert.Equal("Northwind Labs", findings.Employer);
        Assert.Equal("Senior SWE, Payments", findings.Application.Role);
        Assert.Equal("https://www.jobs.example/4021", findings.Application.PostingUrl);
        Assert.Equal($"Senior SWE\n\n{Duties}", findings.Application.Posting);
        Assert.Equal(Arrangement.Hybrid, findings.Application.Arrangement);
        Assert.False(findings.Application.SponsorsVisa);
        Assert.Equal(["Go", "Postgres"], findings.Application.Stack);
        Assert.Equal("fintech · ledgers", findings.Company.About);
        Assert.Equal(
            [("jobs.example", "https://www.jobs.example/4021"), ("board.example", "https://board.example/northwind")],
            findings.Sources.Select(s => (s.Title, s.Url)));
        Assert.Contains("<link>https://www.jobs.example/4021</link>", model.Brief!.User);
        Assert.Contains("<company>Northwind</company>", model.Brief.User);
        Assert.True(model.Brief.Web);
    }

    [Fact]
    public async Task The_posting_is_read_off_the_page_the_model_found_when_the_link_shows_none()
    {
        _pages.Post("https://apply.example/r/77", """<html><body><div id="root"></div><script src="/app.js"></script></body></html>""");
        _pages.Post("https://board.example/jobs/77", Declaring("Platform Engineer", Duties));
        var model = new ScriptedResearchModel(new
        {
            outcome = "read",
            role = "Platform Engineer",
            page = "https://board.example/jobs/77",
            summary = "A platform role.",
        });
        var replies = new RecordingStreamWriter<ReadPostingReply>();

        await _seeker.Research(model, (s, c) =>
            s.ReadPosting(new ReadPostingRequest { Url = "https://apply.example/r/77" }, replies, c));

        var application = Assert.Single(replies.Messages).Findings.Application;
        Assert.Equal($"Platform Engineer\n\n{Duties}", application.Posting);
        Assert.Equal("https://apply.example/r/77", application.PostingUrl);
    }

    [Fact]
    public async Task When_no_page_shows_the_posting_the_models_summary_is_kept()
    {
        var model = new ScriptedResearchModel(new
        {
            outcome = "read",
            role = "Data Engineer",
            page = "https://elsewhere.example/jobs/5",
            summary = "Data Engineer on the billing team.\nWants Spark and SQL.",
        });
        var replies = new RecordingStreamWriter<ReadPostingReply>();

        await _seeker.Research(model, (s, c) =>
            s.ReadPosting(new ReadPostingRequest { Url = "https://gone.example/jobs/5" }, replies, c));

        Assert.Equal(
            "Data Engineer on the billing team.\nWants Spark and SQL.",
            Assert.Single(replies.Messages).Findings.Application.Posting);
        Assert.Contains("https://gone.example/jobs/5", _pages.Requested);
        Assert.Contains("https://elsewhere.example/jobs/5", _pages.Requested);
    }

    [Fact]
    public async Task Pasted_text_is_read_without_the_web_and_kept_as_pasted()
    {
        var model = new ScriptedResearchModel(new
        {
            outcome = "read",
            role = "Staff Engineer",
            page = "https://pasted.example/1",
            summary = "a retyped copy",
            sponsors_visa = "unknown",
            arrangement = "somewhere",
        });
        var replies = new RecordingStreamWriter<ReadPostingReply>();

        await _seeker.Research(model, (s, c) =>
            s.ReadPosting(new ReadPostingRequest { Text = "  Staff Engineer at Corvid  " }, replies, c));

        var application = Assert.Single(replies.Messages).Findings.Application;
        Assert.Equal("Staff Engineer at Corvid", application.Posting);
        Assert.Equal("", application.PostingUrl);
        Assert.False(application.HasSponsorsVisa);
        Assert.Equal(Arrangement.Unspecified, application.Arrangement);
        Assert.False(model.Brief!.Web);
        Assert.Contains("<posting>\nStaff Engineer at Corvid\n</posting>", model.Brief.User);
        Assert.DoesNotContain("https://pasted.example/1", _pages.Requested);
    }

    [Fact]
    public async Task A_posting_needs_a_web_link_or_its_text()
    {
        foreach (var request in new[] { new ReadPostingRequest(), new ReadPostingRequest { Url = "file:///etc/passwd" } })
        {
            var model = new ScriptedResearchModel(new { });

            var error = await Assert.ThrowsAsync<RpcException>(() => _seeker.Research(model, (s, c) =>
                s.ReadPosting(request, new RecordingStreamWriter<ReadPostingReply>(), c)));

            Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
            Assert.Null(model.Brief);
        }
    }

    [Fact]
    public async Task A_page_it_could_not_read_says_why()
    {
        var model = new ScriptedResearchModel(
            new { outcome = "sign_in", role = "ignored" },
            (ResearchAction.Reading, "https://www.linkedin.com/jobs/view/1"));
        var replies = new RecordingStreamWriter<ReadPostingReply>();

        var error = await Assert.ThrowsAsync<RpcException>(() => _seeker.Research(model, (s, c) =>
            s.ReadPosting(new ReadPostingRequest { Url = "https://www.linkedin.com/jobs/view/1" }, replies, c)));

        Assert.Equal(StatusCode.FailedPrecondition, error.StatusCode);
        Assert.Equal(LoopErrors.PostingSignIn, error.Status.Detail);
        Assert.Single(replies.Messages);
        Assert.DoesNotContain("https://www.linkedin.com/jobs/view/1", _pages.Requested);
    }

    [Fact]
    public async Task A_model_failure_reads_as_unavailable()
    {
        var company = await _seeker.CreateCompanyAsync("Brightfield");
        var model = new ScriptedResearchModel(null, (ResearchAction.Searching, "brightfield")) { Fails = true };
        var replies = new RecordingStreamWriter<ResearchCompanyReply>();

        var error = await Assert.ThrowsAsync<RpcException>(() => _seeker.Research(model, (s, c) =>
            s.ResearchCompany(new ResearchCompanyRequest { CompanyId = company.Id }, replies, c)));

        Assert.Equal(StatusCode.Unavailable, error.StatusCode);
        Assert.Equal(LoopErrors.ResearchFailed, error.Status.Detail);
        Assert.Single(replies.Messages);
    }

    private static string Declaring(string title, string description)
    {
        var posting = JsonSerializer.Serialize(new Dictionary<string, string>
        {
            ["@type"] = "JobPosting",
            ["title"] = title,
            ["description"] = $"<p>{description}</p>",
        });
        return $"""<html><head><script type="application/ld+json">{posting}</script></head><body></body></html>""";
    }
}
