using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Scheduling;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;
using ProtoFast.DocumentImport.Engine;
using ProtoFast.DocumentImport.UnitTests.Engine.Fakes;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

/// <summary>Outcomes apply synchronously, so tests can assert policy as soon as a run returns.</summary>
internal sealed class EngineHarness
{
    public const string Family = "invoice";
    public static readonly ContractRef Raw = new("raw", 1);
    public static readonly ContractRef Text = new("text", 1);
    public static readonly ContractRef Markdown = new("markdown", 1);

    public EngineHarness(Action<EngineOptions>? configure = null, double shadowRate = 0)
    {
        Sampler.Rate = shadowRate;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IClassifier>(new FixedClassifier(Family));
        services.AddSingleton<IDiscoveryAgent>(Agent);
        services.AddSingleton<IExecutorFactory>(Executors);
        services.AddSingleton<IShadowSampler>(Sampler);
        services.AddSingleton<IDistiller>(Distiller);
        services.AddSingleton<IRubricVerifierFactory>(new ContentRubricFactory(this));
        services.AddSingleton<IOutcomeBus, SynchronousOutcomeBus>();
        services.AddSingleton<IVerifier>(sp => new ContentVerifier("no-bad", deterministic: true, sp.GetRequiredService<IArtifactStore>()));
        services.AddSingleton<IVerifier>(sp => new ContentVerifier("judge", deterministic: false, sp.GetRequiredService<IArtifactStore>()));
        services.AddAgentWorkflowEngine(o =>
        {
            o.Thresholds = new Thresholds(MinObservations: 3, MineAfterRuns: 3, ShadowSampleRate: shadowRate);
            configure?.Invoke(o);
        });
        Services = services.BuildServiceProvider();
        Executors.Use(Artifacts);
    }

    public FakeTimeProvider Time { get; } = new(new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero));
    public ScriptedAgent Agent { get; } = new();
    public ScriptedExecutorFactory Executors { get; } = new();
    public FixedSampler Sampler { get; } = new();
    public RecordingDistiller Distiller { get; } = new();
    public ServiceProvider Services { get; }

    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();
    public IArtifactStore Artifacts => Get<IArtifactStore>();
    public IRunLedger Ledger => Get<IRunLedger>();
    public IRegistry Registry => Get<IRegistry>();
    public IPolicyStore Policies => Get<IPolicyStore>();
    public IDocumentFamilyPolicyStore Families => Get<IDocumentFamilyPolicyStore>();
    public SynchronousOutcomeBus Outcomes => (SynchronousOutcomeBus)Get<IOutcomeBus>();
    public ExecutorRef Orchestrator => Get<EngineOptions>().Orchestrator;

    public Signature Signature { get; } = new(Family, new Dictionary<string, string>());

    public Task<ArtifactRef> InputAsync(string content = "input") =>
        Artifacts.PutAsync(DocumentImport.Core.DocumentImportIds.New(), ArtifactRef.InputStageId, Utf8(content), Raw, default);

    public async Task<string> ReadAsync(ArtifactRef reference)
    {
        using var reader = new StreamReader(await Artifacts.GetAsync(reference, default));
        return await reader.ReadToEndAsync();
    }

    public async Task<ExecutorRef> DelegateAsync(
        string id, Tier tier, Func<StageRequest, string> produce, ExecutorOrigin origin = ExecutorOrigin.Seed, bool promoted = true)
    {
        var spec = new ExecutorSpec(
            new ExecutorRef(id, 0), tier,
            ModelClasses.For(tier),
            tier == Tier.Codified ? null : new PlaybookRef("pb", 1),
            [],
            tier == Tier.Codified ? $"{id}.dll" : null,
            origin,
            promoted);
        var reference = await Registry.PublishAsync(spec, default);
        Executors.Script(reference, tier, produce);
        return reference;
    }

    public static StageDefinition Stage(string id, params string[] dependsOn) =>
        new(id, dependsOn, Text, Markdown, ["no-bad"], Budget.Unbounded);

    public static Stream Utf8(string content) => new MemoryStream(Encoding.UTF8.GetBytes(content));
}
