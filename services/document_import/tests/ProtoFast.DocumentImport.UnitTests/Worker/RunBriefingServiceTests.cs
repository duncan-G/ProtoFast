using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Briefing;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay;
using ProtoFast.DocumentImport.Screenplay.Briefing;
using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.DocumentImport.UnitTests.Engine.Fakes;
using ProtoFast.DocumentImport.UnitTests.Screenplay;
using ProtoFast.DocumentImport.Worker.Briefing;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Worker;

public class RunBriefingServiceTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_briefing_that_fails_is_recorded_and_waits_while_one_that_succeeds_is_kept()
    {
        var models = new ScriptedLanguageModelFactory();
        models.Script(
            ModelClasses.Medium,
            (_, _) => "I would rather not.",
            (_, _) => """{"outcome": "Stopped without a story.", "overview": "The agent said it was done and was not.", "flags": []}""");
        var services = new ServiceCollection()
            .AddLogging()
            .AddAgentWorkflowEngine()
            .AddInMemoryWorkflowEngineStores()
            .AddScreenplayDiscovery()
            .AddRunBriefing()
            .AddSingleton<ILanguageModelFactory>(models)
            .BuildServiceProvider();

        var ledger = services.GetRequiredService<IRunLedger>();
        await ledger.OpenAsync("run-1", new DocumentSignature("prose", new Dictionary<string, string>()), RunMode.Discovery, Ct);
        await ledger.AppendTranscriptAsync("run-1", 0, """{"message":{"role":"User","text":"Deliver stage `story`."}}""", Ct);
        await ledger.AppendTranscriptAsync("run-1", 1, """{"message":{"role":"Assistant","text":"Done."}}""", Ct);
        await ledger.AbandonAsync("run-1", "stopped short", Ct);

        var briefs = new RecordingRunBriefs(new BriefCandidate("run-1", "prose", RunMode.Discovery, RunStatus.Abandoned, "stopped short"));
        var service = new RunBriefingService(
            briefs, services.GetRequiredService<RunBriefer>(), new RunBriefingOptions(), TimeProvider.System,
            NullLogger<RunBriefingService>.Instance);

        Assert.False(await service.BriefNextAsync(Ct));
        var (runId, error) = Assert.Single(briefs.Failed);
        Assert.Equal("run-1", runId);
        Assert.StartsWith("JsonException:", error);

        Assert.True(await service.BriefNextAsync(Ct));
        Assert.Equal("Stopped without a story.", briefs.Completed["run-1"].Outcome);
        Assert.Contains("Failure: stopped short", models.Calls.Last().User);
        Assert.DoesNotContain(models.Calls, c => c.ModelClass == ModelClasses.Small);
        Assert.False(await service.BriefNextAsync(Ct));
    }
}
