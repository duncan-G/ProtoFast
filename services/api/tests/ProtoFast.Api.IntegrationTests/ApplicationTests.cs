using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Seeker;

namespace ProtoFast.Api.IntegrationTests;

public class ApplicationTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);

    [Fact]
    public async Task The_loop_lists_open_applications_by_stage_then_the_soonest_next_step()
    {
        var closed = NewApplication((await _seeker.CreateCompanyAsync("Lumen Health")).Id, Stage.Tech);
        closed.Closed = true;
        var halcyon = NewApplication((await _seeker.CreateCompanyAsync("Halcyon Pay")).Id, Stage.Onsite);
        halcyon.NextOn = "2026-11-06";
        var northwind = NewApplication((await _seeker.CreateCompanyAsync("Northwind Labs")).Id, Stage.Onsite);
        northwind.NextOn = "2026-11-05";
        var brightfield = NewApplication((await _seeker.CreateCompanyAsync("Brightfield")).Id);
        var corvid = NewApplication((await _seeker.CreateCompanyAsync("Corvid Games")).Id, Stage.Offer);

        foreach (var application in new[] { closed, brightfield, halcyon, corvid, northwind })
        {
            await _seeker.CreateApplicationAsync(application);
        }

        var loop = await _seeker.ListApplicationsAsync();

        Assert.Equal(
            ["Corvid Games", "Northwind Labs", "Halcyon Pay", "Brightfield", "Lumen Health"],
            loop.Select(a => a.Company));
    }

    [Fact]
    public async Task One_not_applied_for_yet_has_no_day_and_lists_after_the_applied()
    {
        var saved = NewApplication((await _seeker.CreateCompanyAsync("Quarry Systems")).Id);
        saved.ClearAppliedOn();
        var created = await _seeker.CreateApplicationAsync(saved);
        await _seeker.CreateApplicationAsync(NewApplication((await _seeker.CreateCompanyAsync("Brightfield")).Id));

        var loop = await _seeker.ListApplicationsAsync();

        Assert.Equal(["Brightfield", "Quarry Systems"], loop.Select(a => a.Company));
        Assert.False(loop[1].HasAppliedOn);

        created.AppliedOn = "2026-11-03";
        await _seeker.UpdateApplicationAsync(created);

        Assert.Equal("2026-11-03", (await _seeker.ListApplicationsAsync())[0].AppliedOn);
    }

    [Fact]
    public async Task An_update_replaces_the_rounds_and_lists()
    {
        var application = NewApplication((await _seeker.CreateCompanyAsync("Halcyon Pay")).Id, Stage.Screen);
        application.Rounds.Add(new Round { Label = "recruiter screen", State = RoundState.Current });
        application.Rounds.Add(new Round { Label = "60-min coding", State = RoundState.Upcoming });
        application.Wants.Add("Go or Java");
        var created = await _seeker.CreateApplicationAsync(application);

        created.Stage = Stage.Onsite;
        created.NextStep = "Virtual onsite, 4 rounds";
        created.NextOn = "2026-11-06";
        created.Rounds.Clear();
        created.Rounds.Add(new Round { Label = "onsite ×4", State = RoundState.Current });
        created.Wants.Clear();
        created.Wants.Add("  Distributed systems  ");
        created.Wants.Add(" ");
        await _seeker.UpdateApplicationAsync(created);

        var stored = Assert.Single(await _seeker.ListApplicationsAsync());
        Assert.Equal(Stage.Onsite, stored.Stage);
        Assert.Equal("2026-11-06", stored.NextOn);
        Assert.Equal("onsite ×4", Assert.Single(stored.Rounds).Label);
        Assert.Equal(["Distributed systems"], stored.Wants);
    }

    [Fact]
    public async Task An_application_keeps_its_source_pay_posting_and_setup()
    {
        var application = NewApplication((await _seeker.CreateCompanyAsync("Halcyon Pay")).Id, Stage.Offer);
        application.Source = Source.Referral;
        application.SourceDetail = "Sam Okafor";
        application.PostingUrl = "https://boards.example.com/halcyon/4021";
        application.Posting = "Senior SWE, Payments Core.\nYou'll own the ledger.";
        application.PostedPay = "$180–220k";
        application.OfferedPay = "$205k";
        application.OfferTerms = "0.1% over 4y · 10% bonus";
        application.Questions.Add("Who owns on-call?");
        application.Stack.Add("Go");
        application.Arrangement = Arrangement.Hybrid;
        application.SponsorsVisa = false;
        var created = await _seeker.CreateApplicationAsync(application);

        created.Arrangement = Arrangement.Unspecified;
        created.ClearSponsorsVisa();
        await _seeker.UpdateApplicationAsync(created);

        var stored = Assert.Single(await _seeker.ListApplicationsAsync());
        Assert.Equal(Source.Referral, stored.Source);
        Assert.Equal("Sam Okafor", stored.SourceDetail);
        Assert.Equal("https://boards.example.com/halcyon/4021", stored.PostingUrl);
        Assert.Equal("Senior SWE, Payments Core.\nYou'll own the ledger.", stored.Posting);
        Assert.Equal("$205k", stored.OfferedPay);
        Assert.Equal(["Who owns on-call?"], stored.Questions);
        Assert.Equal(Arrangement.Unspecified, stored.Arrangement);
        Assert.False(stored.HasSponsorsVisa);
    }

    [Fact]
    public async Task An_application_keeps_its_asked_questions_and_what_was_learned()
    {
        var application = NewApplication((await _seeker.CreateCompanyAsync("Halcyon Pay")).Id, Stage.Tech);
        application.Questions.Add("Who owns on-call?");
        application.Asked.Add(new AskedQuestion { Question = "Is it remote?", Answer = "US only" });
        application.Asked.Add(new AskedQuestion { Question = "  " });
        application.Learnings.Add(new Learning { Text = "Lead with failure modes.", Round = "System design", On = "2026-11-03" });
        application.Learnings.Add(new Learning { Text = "Small PRs." });
        application.Learnings.Add(new Learning { Text = " " });
        await _seeker.CreateApplicationAsync(application);

        var stored = Assert.Single(await _seeker.ListApplicationsAsync());
        Assert.Equal(["Who owns on-call?"], stored.Questions);
        var asked = Assert.Single(stored.Asked);
        Assert.Equal(("Is it remote?", "US only"), (asked.Question, asked.Answer));
        Assert.Equal(2, stored.Learnings.Count);
        Assert.Equal(("Lead with failure modes.", "System design", "2026-11-03"), (stored.Learnings[0].Text, stored.Learnings[0].Round, stored.Learnings[0].On));
        Assert.Equal("", stored.Learnings[1].Round);
        Assert.False(stored.Learnings[1].HasOn);
    }

    [Fact]
    public async Task Learnings_are_capped()
    {
        var application = NewApplication((await _seeker.CreateCompanyAsync("Halcyon Pay")).Id, Stage.Applied);
        application.Learnings.AddRange(Enumerable.Range(0, 201).Select(i => new Learning { Text = $"Note {i}" }));

        var error = await Assert.ThrowsAsync<RpcException>(() => _seeker.CreateApplicationAsync(application));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Theory]
    [InlineData("javascript:alert(1)")]
    [InlineData("boards.example.com/halcyon")]
    public async Task A_posting_link_is_a_web_address(string postingUrl)
    {
        var application = NewApplication((await _seeker.CreateCompanyAsync("Halcyon Pay")).Id);
        application.PostingUrl = postingUrl;

        var error = await Assert.ThrowsAsync<RpcException>(() => _seeker.CreateApplicationAsync(application));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }

    [Theory]
    [InlineData("14 Oct", Stage.Applied)]
    [InlineData("2026-10-14", Stage.Unspecified)]
    public async Task An_application_needs_a_real_day_and_a_stage(string appliedOn, Stage stage)
    {
        var company = await _seeker.CreateCompanyAsync("Quarry Systems");

        var error = await Assert.ThrowsAsync<RpcException>(() =>
            _seeker.CreateApplicationAsync(NewApplication(company.Id, stage, appliedOn)));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
    }
}
