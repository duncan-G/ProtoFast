using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.DocumentImport.IntegrationTests.Fixtures;
using ProtoFast.Storage.Abstractions;
using Xunit;

namespace ProtoFast.DocumentImport.IntegrationTests.Sqs;

public class SqsTraceContextTests(LocalStackFixture localStack) : IAsyncLifetime
{
    private const string QueueKey = "trace-context";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ServiceProvider _services = null!;

    private IMessageQueue Queue => _services.GetRequiredKeyedService<IMessageQueue>(QueueKey);

    public async ValueTask InitializeAsync()
    {
        var services = new ServiceCollection().AddLogging();
        localStack.AddQueue(services, QueueKey, await localStack.CreateFifoQueueAsync(TimeSpan.FromSeconds(1)), TimeSpan.FromSeconds(1));
        _services = services.BuildServiceProvider();
    }

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public async Task A_message_carries_the_sender_s_trace()
    {
        using (var sender = new Activity("send").SetIdFormat(ActivityIdFormat.W3C).Start())
        {
            sender.TraceStateString = "vendor=1";
            await Queue.SendAsync("hello", "group", Ct);

            var message = Assert.Single(await Queue.ReceiveAsync<string>(Ct));

            Assert.Equal(sender.TraceId, message.Parent.TraceId);
            Assert.Equal(sender.SpanId, message.Parent.SpanId);
            Assert.Equal("vendor=1", message.Parent.TraceState);
            Assert.True(message.Parent.IsRemote);
        }
    }

    [Fact]
    public async Task A_message_sent_outside_a_trace_has_no_parent()
    {
        Activity.Current = null;
        await Queue.SendAsync("hello", "group", Ct);

        var message = Assert.Single(await Queue.ReceiveAsync<string>(Ct));

        Assert.Equal(default, message.Parent);
    }
}
