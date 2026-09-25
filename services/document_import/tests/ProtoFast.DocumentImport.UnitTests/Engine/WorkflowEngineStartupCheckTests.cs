using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.Engine.InMemory;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

public class WorkflowEngineStartupCheckTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static WorkflowEngineStartupCheck CheckFor(Action<IServiceCollection> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddAgentWorkflowEngine();
        configure(services);
        return services.BuildServiceProvider().GetServices<IHostedService>().OfType<WorkflowEngineStartupCheck>().Single();
    }

    [Fact]
    public async Task Startup_fails_when_no_stores_are_registered()
    {
        var e = await Assert.ThrowsAsync<InvalidOperationException>(() => CheckFor(_ => { }).StartAsync(Ct));

        Assert.Contains("IRunLedger", e.Message);
        Assert.Contains("IOutcomeQueue", e.Message);
    }

    [Fact]
    public async Task Startup_passes_with_in_memory_stores() =>
        await CheckFor(s => s.AddInMemoryWorkflowEngineStores()).StartAsync(Ct);
}
