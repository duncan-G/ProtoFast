using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>
/// DeepSeek and Kimi (Moonshot), through the OpenAI .NET client pointed at their own base URLs
/// (plan §14.3). One class rather than two because the only thing that differs is the endpoint
/// and the key — and a second copy would be a second place to fix a bug.
/// </summary>
public sealed class OpenAiCompatibleClientFactory(
    string provider,
    IOptionsMonitor<ProviderOptions> options,
    RateLimitHeaderHandler headerHandler) : IProviderClientFactory
{
    public const string DeepSeekKey = "deepseek";
    public const string KimiKey = "kimi";

    public string Provider => provider;

    public IChatClient? Create(ModelDescriptor model)
    {
        var settings = options.Get(provider);
        if (string.IsNullOrWhiteSpace(settings.ApiKey) || string.IsNullOrWhiteSpace(settings.BaseUrl))
        {
            // Both are required: an OpenAI-compatible provider with no base URL would silently
            // send this repository's documents to OpenAI, which is not a provider anyone approved.
            return null;
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(settings.ApiKey),
            new OpenAIClientOptions
            {
                Endpoint = new Uri(settings.BaseUrl),
                Transport = new System.ClientModel.Primitives.HttpClientPipelineTransport(
                    new HttpClient(headerHandler, disposeHandler: false)),
            });

        return client.GetChatClient(model.ModelName).AsIChatClient();
    }
}
