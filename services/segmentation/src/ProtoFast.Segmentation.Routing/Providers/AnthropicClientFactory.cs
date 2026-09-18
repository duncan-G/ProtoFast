using Anthropic;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>
/// Anthropic, through the official SDK's <see cref="IChatClient"/> adapter (plan §14.3).
/// The adapter maps a schema response format onto <c>output_config.format</c>; prompt caching and
/// Message Batches are supported by the provider but not yet used by this pipeline.
/// </summary>
public sealed class AnthropicClientFactory(
    IOptionsMonitor<ProviderOptions> options,
    RateLimitHeaderHandler headerHandler) : IProviderClientFactory
{
    public const string ProviderKey = "anthropic";

    public string Provider => ProviderKey;

    public IChatClient? Create(ModelDescriptor model)
    {
        var settings = options.Get(ProviderKey);
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return null;
        }

        // The SDK owns its HttpClient, so the header handler is supplied as its inner pipeline —
        // that is the only seam where anthropic-ratelimit-* headers are still visible. The
        // default HttpClient is unbounded; a custom one is not, which is why we go through
        // ProviderHttp rather than `new HttpClient(...)`.
        IAnthropicClient client = new AnthropicClient
        {
            ApiKey = settings.ApiKey,
            HttpClient = ProviderHttp.Create(headerHandler),
            // Retries and timeouts belong to the router, which has to see every failed attempt to
            // bill it and to score the model; a second retry loop underneath would hide them.
            MaxRetries = 0,
            // Finite on purpose: the SDK writes Timeout into X-Stainless-Timeout, and
            // InfiniteTimeSpan would go out as -0.001. One minute above the model's
            // RequestTimeout so the router's CTS is still the one that fires.
            Timeout = ProviderHttp.SdkBackstopFor(model.RequestTimeout),
        };

        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            client = client.WithOptions(o => o with { BaseUrl = settings.BaseUrl });
        }

        return client.AsIChatClient(model.ModelName);
    }
}
