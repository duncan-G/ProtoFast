using Anthropic.SDK;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>
/// Anthropic, through the official SDK's <see cref="IChatClient"/> adapter (plan §14.3).
/// Supports prompt caching and Message Batches, both declared in the model's capabilities.
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
        // that is the only seam where anthropic-ratelimit-* headers are still visible.
        var http = new HttpClient(headerHandler, disposeHandler: false);
        if (!string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            http.BaseAddress = new Uri(settings.BaseUrl);
        }

        var client = new AnthropicClient(new APIAuthentication(settings.ApiKey), http);
        return client.Messages.AsBuilder().Build();
    }
}
