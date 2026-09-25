using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoFast.DocumentImport.Data;
using ProtoFast.DocumentImport.Data.Sqs;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Scheduling;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests;

/// <summary>A scheduled run on Postgres, S3 and SQS, from artifacts through to learned policy.</summary>
public class DurableEngineTests(PostgresFixture postgres, LocalStackFixture localStack) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly string _family = $"invoice-{Guid.NewGuid():N}";
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection().AddLogging();
        services.AddSingleton(postgres.DataSource);
        localStack.AddObjectStorage(services, await localStack.CreateBucketAsync());
        localStack.AddQueue(services, SqsOutcomeQueue.QueueKey, await localStack.CreateFifoQueueAsync(TimeSpan.FromSeconds(5)));
        services.AddSingleton<IClassifier>(new FixedFamily(_family));
        services.AddSingleton<IDiscoveryAgent, WritingAgent>();
        services.AddAgentWorkflowEngine();
        services.AddDurableWorkflowEngineStores();
        _services = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task A_scheduled_run_is_recorded_and_its_outcomes_reach_policy()
    {
        var hosted = _services.GetServices<IHostedService>().ToList();
        foreach (var service in hosted)
        {
            await service.StartAsync(Ct);
        }

        try
        {
            var artifacts = _services.GetRequiredService<IArtifactStore>();
            var input = await artifacts.PutAsync(
                Core.DocumentImportIds.New(), ArtifactRef.InputStageId, new MemoryStream("scan"u8.ToArray()), new ContractRef("raw", 1), Ct);
            var stage = new StageDefinition("extract", [], new ContractRef("raw", 1), new ContractRef("text", 1), [], Budget.Unbounded);

            var summary = await _services.GetRequiredService<IScheduler>()
                .RunAsync(new WorkflowDefinition(new WorkflowRef("wf", 1), [stage, stage with { Id = "summarise", DependsOn = ["extract"] }]), input, Ct);

            var stored = await _services.GetRequiredService<IRunLedger>().SummariseAsync(summary.RunId, Ct);
            Assert.Equal(["extract", "summarise"], stored.Stages.Select(s => s.StageId));
            using var reader = new StreamReader(await artifacts.GetAsync(stored.Stages[1].Output, Ct));
            Assert.Equal("wrote summarise", await reader.ReadToEndAsync(Ct));

            var policies = _services.GetRequiredService<IPolicyStore>();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while ((await policies.GetAsync(_family, "summarise", Ct)).Confidence.Observations < 1)
            {
                await Task.Delay(100, timeout.Token);
            }
        }
        finally
        {
            foreach (var service in hosted)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    private sealed class FixedFamily(string family) : IClassifier
    {
        public Task<Signature> ClassifyAsync(ArtifactRef input, CancellationToken ct) =>
            Task.FromResult(new Signature(family, new Dictionary<string, string>()));
    }

    private sealed class WritingAgent : IDiscoveryAgent
    {
        public Task RunAsync(ArtifactRef input, IAgentTools tools, TraceRef trace, CancellationToken ct) => Task.CompletedTask;

        public Task RunStageAsync(StageRequest request, IAgentTools tools, TraceRef trace, CancellationToken ct) =>
            tools.WriteArtifact(request.Stage.Id, new MemoryStream(System.Text.Encoding.UTF8.GetBytes($"wrote {request.Stage.Id}")), request.Stage.Output);
    }
}
