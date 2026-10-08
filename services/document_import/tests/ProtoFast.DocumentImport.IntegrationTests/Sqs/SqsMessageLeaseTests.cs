using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using ProtoFast.Storage;
using ProtoFast.Storage.Abstractions;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Sqs;

public class SqsMessageLeaseTests(LocalStackFixture localStack) : IAsyncLifetime
{
    private const string QueueKey = "lease";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Visibility = TimeSpan.FromSeconds(3);

    private ServiceProvider _services = null!;

    private IMessageQueue Queue => _services.GetRequiredKeyedService<IMessageQueue>(QueueKey);

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection().AddLogging();
        localStack.AddQueue(services, QueueKey, await localStack.CreateQueueAsync(Visibility), Visibility);
        _services = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task A_held_message_stays_off_the_queue_until_the_lease_is_released()
    {
        await Queue.SendAsync("import", "group", Ct);
        var message = Assert.Single(await Queue.ReceiveAsync<string>(Ct));
        var logger = _services.GetRequiredService<ILogger<SqsMessageLeaseTests>>();

        await using (MessageLease.Hold(Queue, message, logger, Ct))
        {
            // Twice the visibility, each receive long-polling for a second of it.
            for (var elapsed = TimeSpan.Zero; elapsed < Visibility * 2; elapsed += TimeSpan.FromSeconds(1))
            {
                Assert.Empty(await Queue.ReceiveAsync<string>(Ct));
            }
        }

        Assert.Equal("import", (await ReceiveAgainAsync()).Body);
    }

    private async Task<QueueMessage<string>> ReceiveAgainAsync()
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if ((await Queue.ReceiveAsync<string>(Ct)).SingleOrDefault() is { } message)
            {
                return message;
            }
        }

        throw new TimeoutException("The message was not redelivered.");
    }
}
