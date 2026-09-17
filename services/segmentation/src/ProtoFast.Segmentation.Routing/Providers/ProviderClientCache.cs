using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;

namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>Hands out one long-lived <see cref="IChatClient"/> per model.</summary>
public interface IProviderClientCache
{
    /// <summary>Null when the model's provider has no usable configuration.</summary>
    IChatClient? Get(ModelDescriptor model);

}

public sealed class ProviderClientCache : IProviderClientCache
{
    private readonly ConcurrentDictionary<string, IChatClient?> _clients = new(StringComparer.Ordinal);
    private readonly Dictionary<string, IProviderClientFactory> _factories;
    private readonly RoutingOptions _options;
    private readonly string _replayPath;

    public ProviderClientCache(
        IEnumerable<IProviderClientFactory> factories,
        IOptions<RoutingOptions> options,
        IOptionsMonitor<ProviderOptions> providerOptions)
    {
        _factories = factories.ToDictionary(f => f.Provider, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
        _replayPath = providerOptions.Get("replay").BaseUrl ?? "tests/recordings";
    }

    public IChatClient? Get(ModelDescriptor model) =>
        _clients.GetOrAdd(model.Key, _ =>
            string.Equals(_options.Mode, "replay", StringComparison.OrdinalIgnoreCase)
                ? new ReplayChatClient(_replayPath, model.ModelName)
                : _factories.TryGetValue(model.Provider, out var factory) ? factory.Create(model) : null);
}
