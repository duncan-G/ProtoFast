using System.Collections.Concurrent;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
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
    private readonly ILoggerFactory _loggerFactory;

    public ProviderClientCache(
        IEnumerable<IProviderClientFactory> factories,
        IOptions<RoutingOptions> options,
        IOptionsMonitor<ProviderOptions> providerOptions,
        ILoggerFactory loggerFactory)
    {
        _factories = factories.ToDictionary(f => f.Provider, StringComparer.OrdinalIgnoreCase);
        _options = options.Value;
        _replayPath = providerOptions.Get("replay").BaseUrl ?? "tests/recordings";
        _loggerFactory = loggerFactory;
    }

    public IChatClient? Get(ModelDescriptor model) =>
        _clients.GetOrAdd(model.Key, _ => Instrument(Create(model)));

    private IChatClient? Create(ModelDescriptor model) =>
        string.Equals(_options.Mode, "replay", StringComparison.OrdinalIgnoreCase)
            ? new ReplayChatClient(_replayPath, model.ModelName)
            : _factories.TryGetValue(model.Provider, out var factory) ? factory.Create(model) : null;

    /// <summary>
    /// The gen_ai.* span every provider gets, added at the one seam every client passes through
    /// rather than in each factory — so a new provider is instrumented the moment it is added, and
    /// so a replay run produces the same spans a live one does. Prompt and completion bodies ride along only when
    /// <see cref="GenAiTelemetry.CaptureMessageContent"/> is set — MEAI reads that variable itself,
    /// so this wrapper takes its default.
    /// </summary>
    private IChatClient? Instrument(IChatClient? client) =>
        client?.AsBuilder().UseOpenTelemetry(_loggerFactory).Build();
}
