using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Segmentation.Core.Options;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Pipeline.Augmentation;
using ProtoFast.Segmentation.Pipeline.Executors;

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
        string pipelineSection = PipelineOptions.SectionName)
    {
        services.Configure<PipelineOptions>(configuration.GetSection(pipelineSection));

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
}
