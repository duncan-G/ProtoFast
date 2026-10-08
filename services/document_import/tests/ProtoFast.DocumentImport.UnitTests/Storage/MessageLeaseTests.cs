using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProtoFast.Storage;
using ProtoFast.Storage.Abstractions;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Storage;

public class MessageLeaseTests
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(15);

    private readonly FakeTimeProvider _time = new();
    private readonly RecordingQueue _queue = new();

    [Fact]
    public async Task The_lease_is_renewed_before_the_timeout_lapses()
    {
        await using var lease = Hold();

        await AdvanceAsync(Timeout);

        Assert.Equal(3, _queue.Renewals.Count);
        Assert.All(_queue.Renewals, renewal => Assert.Equal(("handle", Timeout), renewal));
        Assert.False(lease.IsLost);
        Assert.False(lease.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task A_failed_renewal_is_retried_while_the_message_is_still_invisible()
    {
        _queue.Failures = 1;
        await using var lease = Hold();

        await AdvanceAsync(Timeout);

        // The first tick failed; the next two renewed.
        Assert.Equal(2, _queue.Renewals.Count);
        Assert.False(lease.IsLost);
    }

    [Fact]
    public async Task The_lease_is_lost_once_the_message_has_lapsed_without_a_renewal()
    {
        _queue.Failures = int.MaxValue;
        await using var lease = Hold();

        await AdvanceAsync(Timeout - TimeSpan.FromSeconds(1));
        Assert.False(lease.IsLost);

        await AdvanceAsync(Timeout / 3);

        Assert.True(lease.IsLost);
        Assert.True(lease.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task A_message_held_after_waiting_is_renewed_on_the_time_it_has_left()
    {
        await using var lease = Hold(receivedAt: _time.GetUtcNow() - Timeout + TimeSpan.FromMinutes(1));

        Assert.Single(_queue.Renewals);

        await AdvanceAsync(Timeout / 3);

        Assert.Equal(2, _queue.Renewals.Count);
        Assert.False(lease.IsLost);
    }

    [Fact]
    public async Task A_message_that_lapsed_before_it_was_held_is_lost_on_its_first_failed_renewal()
    {
        _queue.Failures = int.MaxValue;

        await using var lease = Hold(receivedAt: _time.GetUtcNow() - Timeout);

        Assert.True(lease.IsLost);
    }

    [Fact]
    public async Task Releasing_the_lease_stops_the_renewals()
    {
        var lease = Hold();
        await AdvanceAsync(Timeout / 3);
        await lease.DisposeAsync();

        await AdvanceAsync(Timeout);

        Assert.Single(_queue.Renewals);
    }

    [Fact]
    public async Task The_caller_s_cancellation_flows_through_the_token()
    {
        using var caller = new CancellationTokenSource();
        await using var lease = Hold(caller.Token);

        await caller.CancelAsync();

        Assert.True(lease.Token.IsCancellationRequested);
        Assert.False(lease.IsLost);
    }

    private MessageLease Hold(CancellationToken ct = default, DateTimeOffset? receivedAt = null) =>
        MessageLease.Hold(_queue, "handle", Timeout, receivedAt ?? _time.GetUtcNow(), NullLogger.Instance, ct, _time);

    // Each step fires at most one renewal, which runs to completion before the next step.
    private async Task AdvanceAsync(TimeSpan by)
    {
        var step = Timeout / 3;
        for (var advanced = TimeSpan.Zero; advanced < by; advanced += step)
        {
            _time.Advance(TimeSpan.FromTicks(Math.Min(step.Ticks, (by - advanced).Ticks)));
            await Task.Yield();
        }
    }

    private sealed class RecordingQueue : IMessageQueue
    {
        public List<(string Handle, TimeSpan Timeout)> Renewals { get; } = [];

        /// <summary>How many of the next renewals fail.</summary>
        public int Failures { get; set; }

        public string Name => "imports";

        public Task ExtendVisibilityAsync(string receiptHandle, TimeSpan timeout, CancellationToken ct = default)
        {
            if (Failures > 0)
            {
                Failures--;
                throw new InvalidOperationException("SQS is unreachable.");
            }

            Renewals.Add((receiptHandle, timeout));
            return Task.CompletedTask;
        }

        public Task SendAsync<T>(T message, string groupId, CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(CancellationToken ct = default) => throw new NotSupportedException();

        public Task<IReadOnlyList<QueueMessage<T>>> ReceiveAsync<T>(int maxMessages, CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task DeleteAsync(string receiptHandle, CancellationToken ct = default) => throw new NotSupportedException();
    }
}
