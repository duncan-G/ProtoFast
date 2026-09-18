using ProtoFast.Segmentation.Routing.Providers;

namespace ProtoFast.Segmentation.ContractTests;

/// <summary>
/// Per-call deadlines live on the model. The provider HttpClient is unbounded so
/// <see cref="ProtoFast.Segmentation.Routing.ModelDescriptor.RequestTimeout"/> is the timeout
/// that actually fires — and the SDK backstop has to stay finite, because Anthropic puts it on the wire.
/// </summary>
public class RequestTimeoutTests
{
    [Fact]
    public void TheSdkBackstopIsFiniteAndSitsAboveTheModelTimeout()
    {
        var timeout = TimeSpan.FromHours(2);
        var backstop = ProviderHttp.SdkBackstopFor(timeout);

        // InfiniteTimeSpan.TotalSeconds is -0.001, which Anthropic would send as X-Stainless-Timeout.
        Assert.True(backstop > TimeSpan.Zero);
        Assert.True(backstop > timeout);
    }

    [Fact]
    public void TheProviderHttpClientDoesNotCarryTheHundredSecondDefault()
    {
        using var handler = new HttpClientHandler();
        using var client = ProviderHttp.Create(handler);

        Assert.Equal(Timeout.InfiniteTimeSpan, client.Timeout);
    }
}
