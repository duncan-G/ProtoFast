using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.InMemory;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Worker.Import;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Worker;

public class ImportCancellationTests
{
    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(5);

    private readonly FakeTimeProvider _time = new();
    private readonly InMemoryRunLedger _ledger = new();
    private readonly string _uploadId = DocumentImportIds.New();

    [Fact]
    public async Task The_token_is_cancelled_at_the_first_poll_after_the_owner_cancels()
    {
        await _ledger.ReportAsync(_uploadId, new RunProgress(RunPhase.Running, "run"), default);
        await _ledger.ReportAsync(_uploadId, new RunProgress(RunPhase.Cancelled), default);
        await using var cancellation = Watch();
        Assert.False(cancellation.Token.IsCancellationRequested);

        _time.Advance(Interval);
        await WhenCancelledAsync(cancellation.Token);

        Assert.True(cancellation.IsCancelled);
    }

    [Fact]
    public async Task An_import_still_running_is_left_alone()
    {
        await _ledger.ReportAsync(_uploadId, new RunProgress(RunPhase.Running, "run"), default);
        await using var cancellation = Watch();

        _time.Advance(Interval);
        await Task.Delay(50, TestContext.Current.CancellationToken);

        Assert.False(cancellation.Token.IsCancellationRequested);
    }

    [Fact]
    public async Task The_caller_s_cancellation_flows_through_without_counting_as_the_owner_s()
    {
        using var caller = new CancellationTokenSource();
        await using var cancellation = Watch(caller.Token);

        await caller.CancelAsync();

        Assert.True(cancellation.Token.IsCancellationRequested);
        Assert.False(cancellation.IsCancelled);
    }

    private ImportCancellation Watch(CancellationToken ct = default) =>
        ImportCancellation.Watch(_ledger, _uploadId, Interval, NullLogger.Instance, ct, _time);

    private static async Task WhenCancelledAsync(CancellationToken token)
    {
        var cancelled = new TaskCompletionSource();
        await using var registration = token.Register(cancelled.SetResult);
        await cancelled.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
    }
}
