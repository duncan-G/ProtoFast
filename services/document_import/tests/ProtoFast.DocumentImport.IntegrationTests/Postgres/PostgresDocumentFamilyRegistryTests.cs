using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine.Families;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Postgres;

public class PostgresDocumentFamilyRegistryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly string _prefix = $"family-{Guid.NewGuid():N}"[..30];

    private PostgresDocumentFamilyRegistry Registry => new(postgres.Contexts);

    [Fact]
    public async Task Families_are_listed_alphabetically_with_what_was_written_about_them()
    {
        var play = new DocumentFamilyInfo($"{_prefix}-play", "Stage plays", "Plays for the stage.", "classifier", Now, Now);
        var memo = new DocumentFamilyInfo($"{_prefix}-memo", "Memos", "Internal memos.", "op-1", Now, Now);
        await Registry.CreateAsync(play, Ct);
        await Registry.CreateAsync(memo, Ct);

        var listed = (await Registry.ListAsync(Ct)).Where(f => f.Family.StartsWith(_prefix)).ToList();

        Assert.Equal([memo, play], listed);
    }

    [Fact]
    public async Task Registering_a_family_twice_is_refused()
    {
        var info = new DocumentFamilyInfo(_prefix, "Invoices", "", null, Now, Now);
        await Registry.CreateAsync(info, Ct);

        await Assert.ThrowsAsync<InvalidOperationException>(() => Registry.CreateAsync(info, Ct));
    }

    [Fact]
    public async Task An_update_keeps_who_registered_the_family_and_when()
    {
        var info = new DocumentFamilyInfo(_prefix, "Invoices", "One vendor's invoices.", "classifier", Now, Now);
        await Registry.CreateAsync(info, Ct);

        await Registry.UpdateAsync(
            info with { DisplayName = "Vendor invoices", Description = "Invoices from Acme.", CreatedBy = "op-2", UpdatedAt = Now.AddHours(1) }, Ct);

        var updated = Assert.Single(await Registry.ListAsync(Ct), f => f.Family == _prefix);
        Assert.Equal(("Vendor invoices", "Invoices from Acme.", "classifier", Now, Now.AddHours(1)), (updated.DisplayName, updated.Description, updated.CreatedBy, updated.CreatedAt, updated.UpdatedAt));
    }
}
