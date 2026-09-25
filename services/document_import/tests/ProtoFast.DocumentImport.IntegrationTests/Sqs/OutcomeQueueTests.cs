using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.Data.Sqs;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Sqs;

public class OutcomeQueueTests(LocalStackFixture localStack) : IAsyncLifetime
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly RecordingUpdater _updater = new();
    private ServiceProvider _services = null!;

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection().AddLogging();
        localStack.AddQueue(services, SqsOutcomeQueue.QueueKey, await localStack.CreateFifoQueueAsync(TimeSpan.FromSeconds(1)));
        services.AddSingleton<IPolicyUpdater>(_updater);
        services.AddSingleton<SqsOutcomeQueue>();
        services.AddSingleton<OutcomeQueueConsumer>();
        _services = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task A_family_s_outcomes_are_applied_in_the_order_published()
    {
        await PublishAsync("s1", "s2", "s3");

        await ConsumeUntilAsync(() => _updater.Applied.Count == 3);

        Assert.Equal(["s1", "s2", "s3"], _updater.Applied.Select(o => o.StageId));
    }

    [Fact]
    public async Task A_failed_outcome_is_retried_before_the_family_s_next_one()
    {
        _updater.FailOnce = "s1";
        await PublishAsync("s1", "s2");

        await ConsumeUntilAsync(() => _updater.Applied.Count == 2);

        Assert.Equal(["s1", "s2"], _updater.Applied.Select(o => o.StageId));
        Assert.Equal(1, _updater.Failures);
    }

    private async Task PublishAsync(params string[] stageIds)
    {
        var queue = _services.GetRequiredService<SqsOutcomeQueue>();
        foreach (var stageId in stageIds)
        {
            await queue.PublishAsync(
                new Outcome("invoice", stageId, new ExecutorRef("small", 1), OutcomeKind.VerifierPass, 1, DateTimeOffset.UtcNow), Ct);
        }
    }

    private async Task ConsumeUntilAsync(Func<bool> done)
    {
        var consumer = _services.GetRequiredService<OutcomeQueueConsumer>();
        await consumer.StartAsync(Ct);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            while (!done())
            {
                await Task.Delay(100, timeout.Token);
            }
        }
        finally
        {
            await consumer.StopAsync(CancellationToken.None);
        }
    }

    private sealed class RecordingUpdater : IPolicyUpdater
    {
        public ConcurrentQueue<Outcome> Applied { get; } = new();

        public string? FailOnce { get; set; }

        public int Failures;

        public Task ApplyAsync(Outcome outcome, CancellationToken ct)
        {
            if (outcome.StageId == FailOnce)
            {
                FailOnce = null;
                Interlocked.Increment(ref Failures);
                throw new InvalidOperationException("scripted failure");
            }

            Applied.Enqueue(outcome);
            return Task.CompletedTask;
        }
    }
}
