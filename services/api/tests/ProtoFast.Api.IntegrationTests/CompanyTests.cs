using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Seeker;

namespace ProtoFast.Api.IntegrationTests;

public class CompanyTests(StoryDatabase database)
{
    private readonly Seeker _seeker = new(database);
    private readonly Seeker _stranger = new(database);

    [Fact]
    public async Task Companies_list_by_name_ignoring_case()
    {
        foreach (var name in new[] { "northwind Labs", "Corvid Games", "brightfield" })
        {
            await _seeker.CreateCompanyAsync(name);
        }

        var companies = await _seeker.ListCompaniesAsync();

        Assert.Equal(["brightfield", "Corvid Games", "northwind Labs"], companies.Select(c => c.Name));
    }

    [Fact]
    public async Task An_update_replaces_the_people_and_lists()
    {
        var company = new Company { Name = " Halcyon Pay ", About = "fintech · payments api" };
        company.Contacts.Add(new Contact { Name = "Marcus Lee", Role = "recruiter", MetOn = "2026-10-16" });
        company.Values.Add("Ownership");
        company.Competitors.Add("Stripe");
        var created = (await _seeker.Companies((s, c) =>
            s.CreateCompany(new CreateCompanyRequest { Company = company }, c))).Company;

        created.Contacts.Add(new Contact { Name = " Priya Raman ", Role = "eng manager", Note = "Team owns ledger + retries." });
        created.Values.Clear();
        created.Values.Add("  Craft  ");
        created.Values.Add(" ");
        created.Practices.Add("On-call 1 week in 6");
        created.Concerns.Add("Layoffs in March");
        created.LastRaised = "Mar 2025";
        created.Location = " Burbank, CA ";
        await _seeker.UpdateCompanyAsync(created);

        var stored = Assert.Single(await _seeker.ListCompaniesAsync());
        Assert.Equal("Halcyon Pay", stored.Name);
        Assert.Equal("fintech · payments api", stored.About);
        Assert.Equal("Mar 2025", stored.LastRaised);
        Assert.Equal("Burbank, CA", stored.Location);
        Assert.Equal(["Marcus Lee", "Priya Raman"], stored.Contacts.Select(c => c.Name));
        Assert.Equal("2026-10-16", stored.Contacts[0].MetOn);
        Assert.False(stored.Contacts[1].HasMetOn);
        Assert.Equal(["Craft"], stored.Values);
        Assert.Equal(["On-call 1 week in 6"], stored.Practices);
        Assert.Equal(["Stripe"], stored.Competitors);
        Assert.Equal(["Layoffs in March"], stored.Concerns);
    }

    [Fact]
    public async Task A_company_needs_a_name()
    {
        var error = await Assert.ThrowsAsync<RpcException>(() => _seeker.CreateCompanyAsync(" "));

        Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        Assert.Empty(await _seeker.ListCompaniesAsync());
    }

    [Fact]
    public async Task Deleting_a_company_deletes_its_applications_and_notes()
    {
        var quarry = await _seeker.CreateCompanyAsync("Quarry Systems");
        var corvid = await _seeker.CreateCompanyAsync("Corvid Games");
        await _seeker.CreateApplicationAsync(NewApplication(quarry.Id, Stage.Screen));
        await _seeker.CreateApplicationAsync(NewApplication(quarry.Id, Stage.Tech));
        var kept = await _seeker.CreateApplicationAsync(NewApplication(corvid.Id));
        await _seeker.CreateNoteAsync(new Note { Kind = NoteKind.Company, CompanyId = quarry.Id, Title = "Screen" });
        var keptNote = await _seeker.CreateNoteAsync(new Note { Kind = NoteKind.Story, Title = "Late migration" });

        await _seeker.Companies((s, c) => s.DeleteCompany(new DeleteCompanyRequest { CompanyId = quarry.Id }, c));

        Assert.Equal(["Corvid Games"], (await _seeker.ListCompaniesAsync()).Select(c => c.Name));
        Assert.Equal(kept.Id, Assert.Single(await _seeker.ListApplicationsAsync()).Id);
        Assert.Equal(keptNote.Id, Assert.Single(await _seeker.ListNotesAsync()).Id);
    }

    [Fact]
    public async Task The_application_list_carries_its_companys_name()
    {
        var company = await _seeker.CreateCompanyAsync("Halcyon Pay");
        var created = await _seeker.CreateApplicationAsync(NewApplication(company.Id, Stage.Onsite));
        Assert.Equal("Halcyon Pay", created.Company);

        company.Name = "Halcyon";
        await _seeker.UpdateCompanyAsync(company);

        var stored = Assert.Single(await _seeker.ListApplicationsAsync());
        Assert.Equal(company.Id, stored.CompanyId);
        Assert.Equal("Halcyon", stored.Company);
    }

    [Fact]
    public async Task An_application_must_name_one_of_your_own_companies()
    {
        var theirs = await _stranger.CreateCompanyAsync("Halcyon Pay");
        var ours = await _seeker.CreateCompanyAsync("Quarry Systems");
        var application = await _seeker.CreateApplicationAsync(NewApplication(ours.Id));
        application.CompanyId = theirs.Id;

        Task[] attempts =
        [
            _seeker.CreateApplicationAsync(NewApplication(theirs.Id)),
            _seeker.CreateApplicationAsync(NewApplication("not-an-id")),
            _seeker.CreateApplicationAsync(NewApplication("")),
            _seeker.UpdateApplicationAsync(application),
        ];

        foreach (var attempt in attempts)
        {
            var error = await Assert.ThrowsAsync<RpcException>(() => attempt);
            Assert.Equal(StatusCode.InvalidArgument, error.StatusCode);
        }

        Assert.Equal(ours.Id, Assert.Single(await _seeker.ListApplicationsAsync()).CompanyId);
        Assert.Empty(await _stranger.ListApplicationsAsync());
    }
}
