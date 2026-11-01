using System.Collections.Concurrent;
using System.Threading.Channels;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.Storage;
using ProtoFast.Storage.Abstractions;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Storage;

public class QueueConsumerTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly ChannelQueue _queue = new();
    private readonly StoppableLifetime _lifetime = new();

    [Fact]
    public async Task It_takes_no_message_once_the_host_starts_stopping()
    {
        var consumer = new GatedConsumer(_queue, _lifetime);
        await consumer.StartAsync(Ct);
        _queue.Add("first");
        await consumer.Started.Reader.ReadAsync(Ct);
        consumer.Finish("first");

        _lifetime.Stop();
        _queue.Add("second");
        await Task.Delay(200, Ct);

        Assert.Equal(["first"], consumer.Handled);
        await consumer.StopAsync(Ct);
        Assert.Equal(["first"], consumer.Handled);
    }

    [Fact]
    public async Task A_message_in_hand_when_stopping_starts_is_finished()
    {
        var consumer = new GatedConsumer(_queue, _lifetime);
        await consumer.StartAsync(Ct);
        _queue.Add("first");
        await consumer.Started.Reader.ReadAsync(Ct);

        _lifetime.Stop();
        consumer.Finish("first");
        await consumer.Done.Reader.ReadAsync(Ct);
        await consumer.StopAsync(Ct);

        Assert.Equal(["first"], consumer.Handled);
        Assert.Empty(_queue.HandedBack);
    }

    [Fact]
    public async Task A_message_the_stop_cancels_goes_back_on_the_queue_at_once()
    {
        var consumer = new GatedConsumer(_queue, _lifetime);
        await consumer.StartAsync(Ct);
        _queue.Add("first");
        await consumer.Started.Reader.ReadAsync(Ct);

        _lifetime.Stop();
        await consumer.StopAsync(Ct);

        Assert.Empty(consumer.Handled);
        Assert.Equal(["first"], _queue.HandedBack);
    }

    private sealed class GatedConsumer(IMessageQueue queue, IHostApplicationLifetime lifetime)
        : QueueConsumer<string>(queue, lifetime, NullLogger.Instance)
    {
        private readonly ConcurrentDictionary<string, TaskCompletionSource> _gates = new();

        public Channel<string> Started { get; } = Channel.CreateUnbounded<string>();

        public ConcurrentQueue<string> Handled { get; } = new();

        public Channel<string> Done { get; } = Channel.CreateUnbounded<string>();

        public void Finish(string body) => Gate(body).TrySetResult();

        protected override async Task HandleAsync(QueueMessage<string> message, MessageLease lease)
        {
            await Started.Writer.WriteAsync(message.Body);
            await Gate(message.Body).Task.WaitAsync(lease.Token);
            Handled.Enqueue(message.Body);
            await Done.Writer.WriteAsync(message.Body);
        }

        private TaskCompletionSource Gate(string body) =>
            _gates.GetOrAdd(body, _ => new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
    }

    private sealed class ChannelQueue : IMessageQueue
    {
        private readonly Channel<string> _messages = Channel.CreateUnbounded<string>();

        public ConcurrentQueue<string> HandedBack { get; } = new();

        public string Name => "imports";

        public void Add(string body) => _messages.Writer.TryWrite(body);

        public async Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(int maxMessages, CancellationToken ct = default)
        {
            var body = await _messages.Reader.ReadAsync(ct);
            return [(QueueMessage<T>)(object)new QueueMessage<string>(body, body, TimeSpan.FromMinutes(15), DateTimeOffset.UtcNow)];
        }

        public Task ExtendVisibilityAsync(string receiptHandle, TimeSpan timeout, CancellationToken ct = default)
        {
            if (timeout == TimeSpan.Zero)
            {
                HandedBack.Enqueue(receiptHandle);
            }

            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T message, string groupId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(CancellationToken ct = default) => throw new NotSupportedException();

        public Task DeleteAsync(string receiptHandle, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class StoppableLifetime : IHostApplicationLifetime
    {
        private readonly CancellationTokenSource _stopping = new();

        public CancellationToken ApplicationStarted => CancellationToken.None;

        public CancellationToken ApplicationStopping => _stopping.Token;

        public CancellationToken ApplicationStopped => CancellationToken.None;

        public void Stop() => _stopping.Cancel();

        public void StopApplication() => Stop();
    }
}
