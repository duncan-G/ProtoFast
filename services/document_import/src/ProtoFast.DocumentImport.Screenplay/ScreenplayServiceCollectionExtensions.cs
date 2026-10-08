using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Classification;
using ProtoFast.DocumentImport.Screenplay.Executors;
using ProtoFast.DocumentImport.Screenplay.Models;
using ProtoFast.DocumentImport.Screenplay.Tagging;
using ProtoFast.DocumentImport.Screenplay.Verifiers;

namespace ProtoFast.DocumentImport.Screenplay;

public static class ScreenplayServiceCollectionExtensions
{
    /// <summary>
    /// The engine's missing pieces for manuscripts: the synopsis writer and classifier, the discovery
    /// agent and its goal, the model-backed executor factory, the story verifiers, the judge for
    /// rubric verifiers and the mention tagger. Pair with <c>AddAgentWorkflowEngine</c>.
    /// </summary>
    public static IServiceCollection AddScreenplayDiscovery(
        this IServiceCollection services,
        Action<LanguageModelOptions>? configure = null,
        Action<DiscoveryAgentOptions>? configureAgent = null,
        Action<DocumentClassifierOptions>? configureClassifier = null,
        Action<MentionTaggerOptions>? configureTagger = null)
    {
        var options = new LanguageModelOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);

        var agentOptions = new DiscoveryAgentOptions();
        configureAgent?.Invoke(agentOptions);
        services.AddSingleton(agentOptions);
        services.AddSingleton(StoryGoal.Goal);

        var classifierOptions = new DocumentClassifierOptions();
        configureClassifier?.Invoke(classifierOptions);
        services.AddSingleton(classifierOptions);

        var taggerOptions = new MentionTaggerOptions();
        configureTagger?.Invoke(taggerOptions);
        services.AddSingleton(taggerOptions);

        services.TryAddSingleton(TimeProvider.System);
        services.AddHttpClient(GeminiLanguageModel.HttpClientName, http => http.Timeout = TimeSpan.FromMinutes(10));
        services.TryAddSingleton<ILanguageModelFactory, LanguageModelFactory>();

        services.AddSingleton<SynopsisWriter>();
        services.AddSingleton<IDocumentClassifier, LanguageModelDocumentClassifier>();
        services.AddSingleton<IDiscoveryAgent, DiscoveryAgent>();
        services.AddSingleton<IExecutorFactory, LanguageModelExecutorFactory>();
        services.AddSingleton<IVerifier, StoryDraftVerifier>();
        services.AddSingleton<IVerifier, StoryFidelityVerifier>();
        services.AddSingleton<IRubricVerifierFactory, LanguageModelRubricVerifierFactory>();
        services.AddSingleton<MentionTagger>();
        return services;
    }
}
