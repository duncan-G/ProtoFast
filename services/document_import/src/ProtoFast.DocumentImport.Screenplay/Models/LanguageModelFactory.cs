using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ProtoFast.DocumentImport.Engine.Executors;

namespace ProtoFast.DocumentImport.Screenplay.Models;

public sealed class LanguageModelFactory(
    LanguageModelOptions options, IHttpClientFactory httpClients, TimeProvider time, ILogger<LanguageModelFactory> logger)
    : ILanguageModelFactory
{
    private readonly ConcurrentDictionary<string, ILanguageModel> _models = new(StringComparer.Ordinal);

    public ILanguageModel For(string modelClass) => _models.GetOrAdd(modelClass, Create);

    private ILanguageModel Create(string modelClass)
    {
        var primary = Create(options.Default, modelClass);
        if (options.Fallback is not { } fallback
            || string.Equals(fallback, options.Default, StringComparison.OrdinalIgnoreCase)
            || options.Provider(fallback).ApiKey is null)
        {
            return primary;
        }

        return new FallbackLanguageModel(primary, Create(fallback, modelClass), logger);
    }

    private ILanguageModel Create(string providerName, string modelClass)
    {
        var provider = options.Provider(providerName);
        var modelId = modelClass switch
        {
            ModelClasses.Large => provider.Large,
            ModelClasses.Medium => provider.Medium,
            ModelClasses.Small => provider.Small,
            _ => throw new ArgumentException($"'{modelClass}' is not a model class.", nameof(modelClass)),
        };

        ILanguageModel model = providerName.ToLowerInvariant() switch
        {
            LanguageModelProviders.Anthropic => new AnthropicLanguageModel(modelId, provider, options.MaxOutputTokens),
            LanguageModelProviders.Gemini => new GeminiLanguageModel(
                modelId, provider, httpClients.CreateClient(GeminiLanguageModel.HttpClientName), options.MaxOutputTokens),
            _ => throw new InvalidOperationException($"Unknown language model provider '{providerName}'."),
        };

        // Retries sit outside tracing so each attempt is its own span.
        var (telemetryName, serverAddress) = LanguageModelProviders.Telemetry(providerName);
        return new RetryingLanguageModel(
            new TracedLanguageModel(model, telemetryName, serverAddress, options.MaxOutputTokens, options.CaptureMessageContent),
            options.Retry, time, logger);
    }
}
