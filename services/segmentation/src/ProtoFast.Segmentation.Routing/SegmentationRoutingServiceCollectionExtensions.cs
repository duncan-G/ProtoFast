using System.Net.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Segmentation.Routing.Budgets;
using ProtoFast.Segmentation.Routing.Providers;
using StackExchange.Redis;

namespace ProtoFast.Segmentation.Routing;

public static class SegmentationRoutingServiceCollectionExtensions
{
    /// <summary>
    /// Registers the registry, the budget ledger, the provider adapters and the routing client.
    ///
    /// <para>Provider settings are registered as <em>named</em> options so one section
    /// (<c>Seg_Providers__anthropic__ApiKey</c>, …) configures four providers, and so a provider
    /// with no key simply yields no client rather than throwing at startup — a deployment that has
    /// only two of the four keys should run on two providers, not refuse to boot.</para>
    /// </summary>
    public static IServiceCollection AddSegmentationRouting(
        this IServiceCollection services,
        IConfiguration configuration,
        string routingSection = RoutingOptions.SectionName,
        string providersSection = ProviderOptions.SectionName)
    {
        services.Configure<RoutingOptions>(configuration.GetSection(routingSection));

        foreach (var provider in new[]
                 {
                     AnthropicClientFactory.ProviderKey,
                     GeminiProviderKey,
                     OpenAiCompatibleClientFactory.DeepSeekKey,
                     OpenAiCompatibleClientFactory.KimiKey,
                     "replay",
                 })
        {
            services
                .AddOptions<ProviderOptions>(provider)
                .Bind(configuration.GetSection($"{providersSection}:{provider}"));
        }

        services.AddMemoryCache();
        services.TryAddTimeProvider();

        services.AddSingleton(_ => new RateLimitHeaderHandler { InnerHandler = new SocketsHttpHandler() });
        services.AddSingleton<IModelRegistry, ModelRegistry>();
        services.AddSingleton<IBudgetLedger, RedisBudgetLedger>();
        services.AddSingleton<IProviderClientCache, ProviderClientCache>();
        services.AddSingleton<IModelCallLedger, ModelCallLedger>();
        services.AddSingleton<IRoutingChatClient, RoutingChatClient>();

        services.AddSingleton<IProviderClientFactory, AnthropicClientFactory>();
        services.AddSingleton<IProviderClientFactory>(sp => new OpenAiCompatibleClientFactory(
            OpenAiCompatibleClientFactory.DeepSeekKey,
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ProviderOptions>>(),
            sp.GetRequiredService<RateLimitHeaderHandler>()));
        services.AddSingleton<IProviderClientFactory>(sp => new OpenAiCompatibleClientFactory(
            OpenAiCompatibleClientFactory.KimiKey,
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ProviderOptions>>(),
            sp.GetRequiredService<RateLimitHeaderHandler>()));

        // Gemini is reached through its OpenAI-compatible endpoint rather than the Google Gen AI
        // SDK. The plan allows either (§14.3); this way one adapter serves three of the four
        // providers, and the fourth (Anthropic) is the one whose prompt-caching and batch features
        // the pipeline actually uses. Set Seg_Providers__gemini__BaseUrl to the compatibility
        // endpoint; revisit if Vertex-only features are ever needed.
        services.AddSingleton<IProviderClientFactory>(sp => new OpenAiCompatibleClientFactory(
            GeminiProviderKey,
            sp.GetRequiredService<Microsoft.Extensions.Options.IOptionsMonitor<ProviderOptions>>(),
            sp.GetRequiredService<RateLimitHeaderHandler>()));

        return services;
    }

    public const string GeminiProviderKey = "gemini";

    /// <summary>
    /// Redis for the shared budgets. The connection is the same instance auth already uses; only
    /// the <c>seg:budget:</c> keyspace is new (plan §16).
    /// </summary>
    public static IServiceCollection AddSegmentationRedis(this IServiceCollection services, string connectionString)
    {
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect(connectionString));
        return services;
    }

    private static IServiceCollection TryAddTimeProvider(this IServiceCollection services)
    {
        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        return services;
    }
}
