using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Pipeline.Augmentation;
using ProtoFast.Segmentation.Pipeline.Executors;
using ProtoFast.Segmentation.Pipeline.Ingest;
using ProtoFast.Segmentation.Storage;

namespace ProtoFast.Segmentation.Pipeline;

public static class SegmentationPipelineServiceCollectionExtensions
{
    /// <summary>
    /// Registers the prompt assets, the agents, the executors and the workflow host.
    ///
    /// <para>Executors are singletons on purpose: MAF binds them as shared instances, and they
    /// hold no run state — everything a phase needs is addressed by the run id in its message.
    /// Anything that does need a scope (an EF context) resolves one itself through
    /// <see cref="IServiceScopeFactory"/>.</para>
    /// </summary>
    public static IServiceCollection AddSegmentationPipeline(
        this IServiceCollection services,
        IConfiguration configuration,
        string pipelineSection = PipelineOptions.SectionName,
        string conversionSection = ConversionOptions.SectionName)
    {
        services.Configure<PipelineOptions>(configuration.GetSection(pipelineSection));
        services.Configure<ConversionOptions>(configuration.GetSection(conversionSection));

        AddDocumentConverter(services, configuration, conversionSection);

        if (services.All(d => d.ServiceType != typeof(TimeProvider)))
        {
            services.AddSingleton(TimeProvider.System);
        }

        services.AddSingleton<PromptAssets>();
        services.AddSingleton<AgentRunner>();
        services.AddSingleton<FamilyInstincts>();

        services.AddSingleton<LabelerAgent>();
        services.AddSingleton<HeadingLevelAgent>();
        services.AddSingleton<StructurerAgent>();
        services.AddSingleton<StructureWindowerAgent>();
        services.AddSingleton<StructureOrchestratorAgent>();
        services.AddSingleton<WindowBench>();
        services.AddSingleton<StructureOrchestration>();
        services.AddSingleton<CapabilityGapWriter>();
        services.AddSingleton<StructureReviewerAgent>();
        services.AddSingleton<AugmenterAgent>();

        services.AddSingleton<RunArtifacts>();
        services.AddSingleton<RunJournal>();
        services.AddSingleton<PhaseGate>();
        services.AddSingleton<HumanGatePolicy>();

        services.AddSingleton<IngestExecutor>();
        services.AddSingleton<CleanExecutor>();
        services.AddSingleton<TriageExecutor>();
        services.AddSingleton<LabelExecutor>();
        services.AddSingleton<AssembleExecutor>();
        services.AddSingleton<StructureExecutor>();
        services.AddSingleton<ValidateExecutor>();
        services.AddSingleton<StructureReviewExecutor>();
        services.AddSingleton<HumanGateExecutor>();
        services.AddSingleton<GateResumeExecutor>();
        services.AddSingleton<FreezeExecutor>();
        services.AddSingleton<AugmentExecutor>();

        services.AddSingleton<SegmentationWorkflowFactory>();
        services.AddSingleton<IWorkflowHost, WorkflowHost>();

        // Augmentation types are registered as a set so a new type is one AddSingleton away and
        // the catalogue needs no edit (plan §12.1).
        services.AddSingleton<IAugmentationType, KeyPointsAugmentation>();
        services.AddSingleton<IAugmentationCatalogue, AugmentationCatalogue>();

        return services;
    }

    /// <summary>
    /// Registers the conversion sidecar's client (ingest plan §9.1, §17.1).
    ///
    /// <para>The timeout is deliberately longer than the converter's own 8-minute budget, so a slow
    /// conversion ends with the converter's structured error rather than with a client-side
    /// cancellation that says nothing about why. An unset endpoint is not an error here — a fresh
    /// clone has no converter and Markdown uploads still run end to end — but a non-Markdown upload
    /// then fails phase 0 with a message that names the reason.</para>
    /// </summary>
    private static void AddDocumentConverter(
        IServiceCollection services, IConfiguration configuration, string conversionSection)
    {
        var endpoint = configuration[$"{conversionSection}:Endpoint"];

        var timeout = TimeSpan.TryParse(configuration[$"{conversionSection}:Timeout"], out var configured)
            ? configured
            : TimeSpan.FromMinutes(10);

        services.AddHttpClient<IDocumentConverter, DocumentConverter>(client =>
        {
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                // The trailing slash matters: without it a relative "convert" would replace the
                // last path segment of the base address rather than being appended to it.
                client.BaseAddress = new Uri(endpoint.TrimEnd('/') + "/");
            }

            client.Timeout = timeout;
        });
    }
}
