using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using ProtoFast.DocumentImport.Screenplay.Models;
using Xunit;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

public sealed class RetryingLanguageModelTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private readonly FakeTimeProvider _time = new();

    private RetryingLanguageModel Retrying(ILanguageModel inner, TimeSpan? maxWait = null) => new(
        inner,
        new ModelRetryOptions
        {
            InitialDelay = TimeSpan.FromSeconds(1),
            MaxDelay = TimeSpan.FromSeconds(4),
            MaxWait = maxWait ?? TimeSpan.FromSeconds(10),
        },
        _time,
        NullLogger.Instance);

    [Fact]
    public async Task An_outage_that_ends_inside_the_window_is_waited_out()
    {
        var inner = Flaky(failures: 3, () => new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable));

        var reply = await DriveAsync(Retrying(inner).CompleteAsync("s", "u", Ct));

        Assert.Equal("ok", reply.Text);
        Assert.Equal(4, inner.Calls);
        Assert.Equal([1, 2, 4], inner.GapsInSeconds());
    }

    [Fact]
    public async Task An_outage_that_outlasts_the_window_is_unavailable()
    {
        var inner = Flaky(failures: int.MaxValue, () => new HttpRequestException("down", null, HttpStatusCode.BadGateway));

        var unavailable = await Assert.ThrowsAsync<LanguageModelUnavailableException>(
            () => DriveAsync(Retrying(inner).ConverseAsync("s", [ChatMessage.User("u")], [], Ct)));

        // Waits of 1, 2 and 4 seconds fit in 10; a fourth of 4 would not.
        Assert.Equal(4, inner.Calls);
        Assert.Equal("flaky", unavailable.ModelId);
        Assert.IsType<HttpRequestException>(unavailable.InnerException);
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task A_rejected_request_is_not_retried(HttpStatusCode status)
    {
        var inner = Flaky(failures: 1, () => new HttpRequestException("no", null, status));

        await Assert.ThrowsAsync<HttpRequestException>(() => Retrying(inner).CompleteAsync("s", "u", Ct));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_refusal_is_not_retried()
    {
        var inner = Flaky(failures: 1, () => new LanguageModelRefusedException("flaky", "cyber"));

        await Assert.ThrowsAsync<LanguageModelRefusedException>(() => Retrying(inner).CompleteAsync("s", "u", Ct));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_a_timeout()
    {
        using var cts = new CancellationTokenSource();
        var inner = Flaky(failures: 1, () =>
        {
            cts.Cancel();
            return new OperationCanceledException(cts.Token);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Retrying(inner).CompleteAsync("s", "u", cts.Token));

        Assert.Equal(1, inner.Calls);
    }

    [Fact]
    public async Task A_provider_timeout_is_retried()
    {
        var inner = Flaky(failures: 1, () => new TaskCanceledException("timed out", new TimeoutException()));

        var reply = await DriveAsync(Retrying(inner).CompleteAsync("s", "u", Ct));

        Assert.Equal(("ok", 2), (reply.Text, inner.Calls));
    }

    [Fact]
    public async Task Zero_wait_disables_retries()
    {
        var inner = Flaky(failures: 1, () => new HttpRequestException("down", null, HttpStatusCode.ServiceUnavailable));

        await Assert.ThrowsAsync<LanguageModelUnavailableException>(() => Retrying(inner, TimeSpan.Zero).CompleteAsync("s", "u", Ct));

        Assert.Equal(1, inner.Calls);
    }

    // Advances the fake clock a second at a time until the call settles.
    private async Task<LanguageModelReply> DriveAsync(Task<LanguageModelReply> call)
    {
        while (!call.IsCompleted)
        {
            await Task.Delay(1, Ct);
            _time.Advance(TimeSpan.FromSeconds(1));
        }

        return await call;
    }

    private FlakyModel Flaky(int failures, Func<Exception> failure) => new(_time, failures, failure);

    private sealed class FlakyModel(TimeProvider time, int failures, Func<Exception> failure) : ILanguageModel
    {
        private readonly List<DateTimeOffset> _calledAt = [];

        public int Calls => _calledAt.Count;

        public string ModelId => "flaky";

        public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
        {
            _calledAt.Add(time.GetUtcNow());
            return Calls <= failures
                ? Task.FromException<LanguageModelReply>(failure())
                : Task.FromResult(new LanguageModelReply("ok", ModelId, 1, 1, 0));
        }

        public Task<LanguageModelReply> ConverseAsync(
            string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
            CompleteAsync(system, "", ct);

        public int[] GapsInSeconds() => _calledAt.Zip(_calledAt.Skip(1), (a, b) => (int)(b - a).TotalSeconds).ToArray();
    }
}
