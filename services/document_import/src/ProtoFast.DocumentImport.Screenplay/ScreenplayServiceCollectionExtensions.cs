using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Skills;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Briefing;
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

    /// <summary>
    /// The checks every family's skills pass before they are published, so a skill fits the family and
    /// not the one manuscript it was learned on. Pair with <c>AddScreenplayDiscovery</c> for its models.
    /// </summary>
    public static IServiceCollection AddSkillVerification(
        this IServiceCollection services, Action<SkillVerificationOptions>? configure = null)
    {
        var options = new SkillVerificationOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<ISkillVerifier, SkillNamesVerifier>();
        services.AddSingleton<ISkillVerifier, SkillGeneralityVerifier>();
        return services;
    }

    /// <summary>
    /// The review every script the agent writes passes before it is stored, on an Anthropic model.
    /// Pair with <c>AddScreenplayDiscovery</c> for its models.
    /// </summary>
    public static IServiceCollection AddScriptSafetyReview(
        this IServiceCollection services, Action<ScriptSafetyOptions>? configure = null)
    {
        var options = new ScriptSafetyOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<IScriptSafetyReviewer, ScriptSafetyReviewer>();
        return services;
    }

    /// <summary>
    /// The run briefer, which writes what a reviewer reads about a finished run. Pair with
    /// <c>AddScreenplayDiscovery</c> for its models; the worker's briefing service drives it.
    /// </summary>
    public static IServiceCollection AddRunBriefing(this IServiceCollection services, Action<RunBriefingOptions>? configure = null)
    {
        var options = new RunBriefingOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddSingleton<RunBriefer>();
        return services;
    }
}
