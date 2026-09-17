using Microsoft.Extensions.AI;

namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>
/// Builds the underlying <see cref="IChatClient"/> for one model. Implementations exist per
/// provider; the router never sees them, and no executor may construct one (plan §14.1).
/// </summary>
public interface IProviderClientFactory
{
    /// <summary>Provider key this factory serves: <c>anthropic</c>, <c>gemini</c>, ….</summary>
    string Provider { get; }

    /// <summary>Null when the provider has no API key configured; the router then skips its models.</summary>
    IChatClient? Create(ModelDescriptor model);
}
