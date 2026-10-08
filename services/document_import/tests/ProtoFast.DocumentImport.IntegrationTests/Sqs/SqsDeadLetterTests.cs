using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using ProtoFast.Storage.Abstractions;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Sqs;

public class SqsDeadLetterTests(LocalStackFixture localStack)
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static readonly TimeSpan Visibility = TimeSpan.FromSeconds(1);

    [Fact]
    public async Task The_delivery_that_reaches_the_receive_limit_is_the_last()
    {
        var queue = await QueueAsync(maxReceiveCount: 2);
        await queue.SendAsync("import", "group", Ct);

        var first = Assert.Single(await queue.ReceiveAsync<string>(Ct));
        var second = await ReceiveAgainAsync(queue);

        Assert.False(first.IsLastDelivery);
        Assert.True(second.IsLastDelivery);
    }

    [Fact]
    public async Task A_queue_without_a_dead_letter_queue_never_gives_up()
    {
        var queue = await QueueAsync(maxReceiveCount: null);
        await queue.SendAsync("import", "group", Ct);

        Assert.False(Assert.Single(await queue.ReceiveAsync<string>(Ct)).IsLastDelivery);
        Assert.False((await ReceiveAgainAsync(queue)).IsLastDelivery);
    }

    private async Task<IMessageQueue> QueueAsync(int? maxReceiveCount)
    {
        var services = new ServiceCollection().AddLogging();
        localStack.AddQueue(services, "dead-letter", await localStack.CreateQueueAsync(Visibility, maxReceiveCount), Visibility);
        return services.BuildServiceProvider().GetRequiredKeyedService<IMessageQueue>("dead-letter");
    }

    // Left undeleted, the message comes back once its visibility timeout lapses.
    private static async Task<QueueMessage<string>> ReceiveAgainAsync(IMessageQueue queue)
    {
        for (var attempt = 0; attempt < 10; attempt++)
        {
            if ((await queue.ReceiveAsync<string>(Ct)).SingleOrDefault() is { } message)
            {
                return message;
            }
        }

        throw new TimeoutException("The message was not redelivered.");
    }
}
