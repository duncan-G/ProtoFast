using System.Net;
using Microsoft.Extensions.AI;
using ProtoFast.Segmentation.Routing.Providers;

namespace ProtoFast.Segmentation.ContractTests;

/// <summary>
/// The adapters against recorded provider responses, including the 429s and the malformed JSON
/// (plan §26.6).
///
/// <para>These are the cases that only ever happen in production at 3am, and the ones the retry
/// policy, the circuit breaker and the repair loop all branch on. Recording them is the only way
/// to exercise that branching without a provider outage.</para>
/// </summary>
public class ProviderContractTests
{
    [Fact]
    public async Task ARecordedReplyIsReplayedWithItsUsage()
    {
        using var scope = new RecordingScope();
        const string prompt = "label these lines";

        scope.Add(prompt, new ReplayChatClient.Recording("""{"window":0,"labels":[]}""")
        {
            InputTokens = 1200,
            OutputTokens = 40,
        });

        var response = await scope.Client().GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: TestContext.Current.CancellationToken);

        Assert.Equal("""{"window":0,"labels":[]}""", response.Text);
        Assert.Equal(1200, response.Usage?.InputTokenCount);
        Assert.Equal(40, response.Usage?.OutputTokenCount);
    }

    [Fact]
    public async Task ARecordedRateLimitIsRetryableAndCarriesRetryAfter()
    {
        using var scope = new RecordingScope();
        const string prompt = "label these lines";

        scope.Add(prompt, new ReplayChatClient.Recording(string.Empty)
        {
            ErrorKind = ProviderErrorKind.RateLimited,
            ErrorMessage = "rate limited",
            RetryAfterSeconds = 12,
        });

        var failure = await Assert.ThrowsAsync<ProviderException>(
            () => scope.Client().GetResponseAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            cancellationToken: TestContext.Current.CancellationToken));

        Assert.Equal(ProviderErrorKind.RateLimited, failure.Kind);
        Assert.True(failure.IsRetryable);
        Assert.Equal(TimeSpan.FromSeconds(12), failure.RetryAfter);
    }

    [Fact]
    public async Task AMissingRecordingFailsLoudlyRatherThanSilently()
    {
        using var scope = new RecordingScope();

        var failure = await Assert.ThrowsAsync<ProviderException>(
            () => scope.Client().GetResponseAsync(
                [new ChatMessage(ChatRole.User, "never recorded")],
                cancellationToken: TestContext.Current.CancellationToken));

        Assert.Contains("No recording for prompt hash", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheRecordingKeyChangesWithThePrompt()
    {
        var first = ReplayChatClient.KeyFor([new ChatMessage(ChatRole.User, "one")]);
        var second = ReplayChatClient.KeyFor([new ChatMessage(ChatRole.User, "two")]);

        Assert.NotEqual(first, second);
        Assert.Equal(first, ReplayChatClient.KeyFor([new ChatMessage(ChatRole.User, "one")]));
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests, ProviderErrorKind.RateLimited)]
    [InlineData(HttpStatusCode.ServiceUnavailable, ProviderErrorKind.Overloaded)]
    [InlineData(HttpStatusCode.BadGateway, ProviderErrorKind.Overloaded)]
    [InlineData(HttpStatusCode.RequestTimeout, ProviderErrorKind.Timeout)]
    [InlineData(HttpStatusCode.Unauthorized, ProviderErrorKind.Auth)]
    [InlineData(HttpStatusCode.Forbidden, ProviderErrorKind.Auth)]
    [InlineData(HttpStatusCode.BadRequest, ProviderErrorKind.BadRequest)]
    public void HttpStatusIsClassifiedConsistentlyAcrossProviders(
        HttpStatusCode status, ProviderErrorKind expected)
    {
        var classified = ProviderException.From(new HttpRequestException("failed", null, status), "anthropic");

        Assert.Equal(expected, classified.Kind);
    }

    [Fact]
    public void AContextOverflowIsDistinguishedFromAnOrdinaryBadRequest()
    {
        // Both arrive as HTTP 400, and they need opposite responses: one re-plans the window, the
        // other is a bug in the prompt. Only the message separates them (plan §14.7).
        var overflow = ProviderException.From(
            new HttpRequestException("maximum context length is 200000 tokens", null, HttpStatusCode.BadRequest),
            "deepseek");

        Assert.Equal(ProviderErrorKind.ContextTooLong, overflow.Kind);
        Assert.False(overflow.IsRetryable);
    }

    [Fact]
    public void AnthropicsOverloadedStatusIsRecognised()
    {
        // 529 has no HttpStatusCode name, so it has to be handled by number or it falls through
        // to Unknown and stops being retryable.
        var overloaded = ProviderException.From(
            new HttpRequestException("overloaded", null, (HttpStatusCode)529), "anthropic");

        Assert.Equal(ProviderErrorKind.Overloaded, overloaded.Kind);
        Assert.True(overloaded.IsRetryable);
    }

    [Fact]
    public void ACancelledCallIsATimeoutRatherThanAnUnknownFailure()
    {
        var timeout = ProviderException.From(new OperationCanceledException(), "gemini");

        Assert.Equal(ProviderErrorKind.Timeout, timeout.Kind);
        Assert.True(timeout.IsRetryable);
    }

    [Theory]
    [InlineData("6m0s", 360)]
    [InlineData("1.5s", 1.5)]
    [InlineData("30", 30)]
    [InlineData("1h2m3s", 3723)]
    public void OpenAiStyleResetDurationsAreParsed(string header, double expectedSeconds)
    {
        var parsed = RateLimitHeaderHandler.ParseDuration(header);

        Assert.NotNull(parsed);
        Assert.Equal(expectedSeconds, parsed!.Value.TotalSeconds, 3);
    }
}
