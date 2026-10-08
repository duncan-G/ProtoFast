using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Data.Postgres;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Families;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Postgres;

public class PostgresDocumentFamilyDirectoryTests(PostgresFixture postgres)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly DateTimeOffset Now = new(2026, 10, 7, 12, 0, 0, TimeSpan.Zero);

    private readonly string _family = $"invoice-{Guid.NewGuid():N}"[..40];

    private PostgresDocumentFamilyDirectory Directory => new(postgres.Contexts, TimeProvider.System);

    private PostgresDocumentFamilyRegistry Registry => new(postgres.Contexts);

    private PostgresRunLedger Ledger => new(postgres.Contexts, TimeProvider.System);

    private PostgresDocumentFamilyCatalog Catalog => new(postgres.Contexts, TimeProvider.System);

    private PostgresDocumentFamilyGenerations Generations => new(postgres.Contexts, TimeProvider.System);

    [Fact]
    public async Task A_family_the_classifier_created_is_listed_without_info_and_counts_its_runs()
    {
        var runId = DocumentImportIds.New();
        await Ledger.OpenAsync(runId, Signature(_family), RunMode.Discovery, Ct);
        await Catalog.AddSkillAsync(_family, new SkillRef("split", 1), Ct);

        var listed = Assert.Single(await Directory.ListAsync(Ct), f => f.Family == _family);

        Assert.Null(listed.Info);
        Assert.Equal((0, RunMode.Discovery, 1, 1, 1, 0), (listed.Generation, listed.Mode, listed.Runs, listed.OpenRuns, listed.Skills, listed.Executors));
        Assert.NotNull(listed.LastRunAt);
    }

    [Fact]
    public async Task A_removed_skill_stays_in_the_family_history_but_is_not_counted()
    {
        await Catalog.AddSkillAsync(_family, new SkillRef("split", 1), Ct);
        await Catalog.AddSkillAsync(_family, new SkillRef("outline", 1), Ct);
        await Catalog.RemoveSkillAsync(_family, "split", "Hardcodes one title.", Ct);

        var listed = Assert.Single(await Directory.ListAsync(Ct), f => f.Family == _family);
        var detail = await Directory.FindAsync(_family, null, Ct);

        Assert.Equal(1, listed.Skills);
        var split = Assert.Single(detail!.Skills, s => s.Ref.Id == "split");
        Assert.NotNull(split.RemovedAt);
        Assert.Equal("Hardcodes one title.", split.RemovalReason);
        Assert.Null(Assert.Single(detail.Skills, s => s.Ref.Id == "outline").RemovedAt);
    }

    [Fact]
    public async Task A_reset_family_shows_its_current_generation_and_sums_runs_across_them()
    {
        await Ledger.OpenAsync(DocumentImportIds.New(), Signature(_family), RunMode.Discovery, Ct);
        await Generations.ResetAsync(_family, Ct);
        await Ledger.OpenAsync(DocumentImportIds.New(), Signature($"{_family}#1"), RunMode.Discovery, Ct);
        await Catalog.AddSkillAsync(_family, new SkillRef("old", 1), Ct);
        await Catalog.AddSkillAsync($"{_family}#1", new SkillRef("new", 1), Ct);
        await Catalog.AddVerifierAsync($"{_family}#1", new VerifierSpec("shape", "story", "well formed"), Ct);
        await new PostgresPolicyStore(postgres.Contexts, new EngineOptions(), TimeProvider.System).PutAsync(
            PolicyRow.Default($"{_family}#1", "story", new ExecutorRef("stage-agent", 1), Now), Ct);

        var listed = Assert.Single(await Directory.ListAsync(Ct), f => f.Family == _family);
        var current = await Directory.FindAsync(_family, null, Ct);
        var previous = await Directory.FindAsync(_family, 0, Ct);

        Assert.Equal((1, 2, 1, 1), (listed.Generation, listed.Runs, listed.Skills, listed.Verifiers));
        Assert.Equal((1, 1), (current!.CurrentGeneration, current.Generation));
        Assert.Equal("new", Assert.Single(current.Skills).Ref.Id);
        Assert.Equal("story", Assert.Single(current.StagePolicies).StageId);
        Assert.Equal([0, 1], current.RunsByGeneration.Select(g => g.Generation));
        Assert.Equal("old", Assert.Single(previous!.Skills).Ref.Id);
        Assert.Equal(1, previous.CurrentGeneration);
    }

    [Fact]
    public async Task An_operator_registers_a_family_ahead_of_any_run_and_edits_it()
    {
        var info = new DocumentFamilyInfo(_family, "Invoices", "One vendor's invoices.", "op-1", Now, Now);
        await Registry.CreateAsync(info, Ct);

        var created = await Directory.FindAsync(_family, null, Ct);
        await Registry.UpdateAsync(info with { DisplayName = "Vendor invoices", UpdatedAt = Now.AddHours(1) }, Ct);
        var edited = await Directory.FindAsync(_family, null, Ct);

        Assert.Equal(info, created!.Info);
        Assert.Equal(RunMode.Discovery, created.Policy.Mode);
        Assert.Equal(("Vendor invoices", "op-1", Now.AddHours(1)), (edited!.Info!.DisplayName, edited.Info.CreatedBy, edited.Info.UpdatedAt));
        Assert.Contains(await Directory.ListAsync(Ct), f => f.Family == _family && f.Info?.DisplayName == "Vendor invoices");
    }

    [Fact]
    public async Task Describing_a_family_only_the_ledger_knew_registers_it()
    {
        await Ledger.OpenAsync(DocumentImportIds.New(), Signature(_family), RunMode.Discovery, Ct);

        await Registry.UpdateAsync(new DocumentFamilyInfo(_family, "Memos", "Internal memos.", "op-2", Now, Now), Ct);

        var found = await Directory.FindAsync(_family, null, Ct);
        Assert.Equal(("Memos", "op-2"), (found!.Info!.DisplayName, found.Info.CreatedBy));
    }

    [Fact]
    public async Task An_unknown_family_is_null()
    {
        Assert.Null(await Directory.FindAsync(_family, null, Ct));
    }

    private static DocumentSignature Signature(string family) => new(family, new Dictionary<string, string>());
}
