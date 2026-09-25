using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using ProtoFast.DocumentImport.Engine;

namespace ProtoFast.DocumentImport.UnitTests.Engine;

/// <summary>
/// The engine wired with in-memory stores and scriptable fakes. Outcomes are applied to the
/// updater synchronously, so a test can assert policy as soon as a run returns.
/// </summary>
internal sealed class EngineHarness
{
    public const string Bucket = "invoice";
    public static readonly ContractRef Raw = new("raw", 1);
    public static readonly ContractRef Text = new("text", 1);
    public static readonly ContractRef Markdown = new("markdown", 1);

    public EngineHarness(Action<EngineOptions>? configure = null, double shadowRate = 0)
    {
        Sampler.Rate = shadowRate;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<TimeProvider>(Time);
        services.AddSingleton<IClassifier>(new FixedClassifier(Bucket));
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
    public IBucketPolicyStore Buckets => Get<IBucketPolicyStore>();
    public SynchronousOutcomeBus Outcomes => (SynchronousOutcomeBus)Get<IOutcomeBus>();
    public ExecutorRef Orchestrator => Get<EngineOptions>().Orchestrator;

    public Signature Signature { get; } = new(Bucket, new Dictionary<string, string>());

    public Task<ArtifactRef> InputAsync(string content = "input") =>
        Artifacts.PutAsync(DocumentImport.Core.DocumentImportIds.New(), ArtifactRef.InputStageId, Utf8(content), Raw, default);

    public async Task<string> ReadAsync(ArtifactRef reference)
    {
        using var reader = new StreamReader(await Artifacts.GetAsync(reference, default));
        return await reader.ReadToEndAsync();
    }

    /// <summary>Publishes a promoted seed executor at a delegate tier whose output is scripted.</summary>
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

internal sealed class FixedClassifier(string bucket) : IClassifier
{
    public Task<Signature> ClassifyAsync(ArtifactRef input, CancellationToken ct) =>
        Task.FromResult(new Signature(bucket, new Dictionary<string, string>()));
}

internal sealed class FixedSampler : IShadowSampler
{
    public double Rate { get; set; }
    public bool Take(double rate) => Rate >= 1;
}

internal sealed class RecordingDistiller : IDistiller
{
    public ConcurrentQueue<PolicyRow> Requests { get; } = new();

    public Task RequestCandidateAsync(PolicyRow row, CancellationToken ct)
    {
        Requests.Enqueue(row);
        return Task.CompletedTask;
    }
}

/// <summary>Applies each outcome before returning, one at a time, as the single writer would.</summary>
internal sealed class SynchronousOutcomeBus(IServiceProvider services) : IOutcomeBus
{
    private readonly SemaphoreSlim _writer = new(1, 1);

    public ConcurrentQueue<Outcome> Published { get; } = new();

    public async Task PublishAsync(Outcome outcome, CancellationToken ct)
    {
        Published.Enqueue(outcome);
        await _writer.WaitAsync(ct);
        try
        {
            await services.GetRequiredService<IPolicyUpdater>().ApplyAsync(outcome, ct);
        }
        finally
        {
            _writer.Release();
        }
    }
}

/// <summary>Fails content containing "bad", degrades content containing "meh".</summary>
internal sealed class ContentVerifier(string id, bool deterministic, IArtifactStore artifacts) : IVerifier
{
    public string Id => id;
    public bool IsDeterministic => deterministic;
    public ConcurrentQueue<ArtifactRef> Seen { get; } = new();

    public async Task<VerifierResult> VerifyAsync(StageRequest request, StageResult result, CancellationToken ct)
    {
        Seen.Enqueue(result.Output);
        using var reader = new StreamReader(await artifacts.GetAsync(result.Output, ct));
        var content = await reader.ReadToEndAsync(ct);
        var verdict = content.Contains("bad") ? Verdict.Fail : content.Contains("meh") ? Verdict.Degraded : Verdict.Pass;
        return new VerifierResult(id, verdict, verdict.ToString(), []);
    }
}

internal sealed class ContentRubricFactory(EngineHarness harness) : IRubricVerifierFactory
{
    public IVerifier Create(VerifierSpec spec) => new ContentVerifier(spec.Id, deterministic: false, harness.Artifacts);
}

internal sealed class ScriptedExecutorFactory : IExecutorFactory
{
    private readonly ConcurrentDictionary<ExecutorRef, (Tier Tier, Func<StageRequest, string> Produce)> _scripts = new();
    private IArtifactStore? _artifacts;

    public ConcurrentQueue<(ExecutorRef Executor, string StageId)> Calls { get; } = new();

    public void Script(ExecutorRef executor, Tier tier, Func<StageRequest, string> produce) =>
        _scripts[executor] = (tier, produce);

    public void Use(IArtifactStore artifacts) => _artifacts = artifacts;

    public bool CanBuild(ExecutorSpec spec) => _scripts.ContainsKey(spec.Ref);

    public Task<IExecutor> BuildAsync(ExecutorSpec spec, CancellationToken ct) =>
        Task.FromResult<IExecutor>(new ScriptedExecutor(spec.Ref, _scripts[spec.Ref], this));

    private sealed class ScriptedExecutor(
        ExecutorRef reference, (Tier Tier, Func<StageRequest, string> Produce) script, ScriptedExecutorFactory owner) : IExecutor
    {
        public Tier Tier => script.Tier;

        public async Task<StageResult> ExecuteAsync(StageRequest request, CancellationToken ct)
        {
            owner.Calls.Enqueue((reference, request.Stage.Id));
            var content = script.Produce(request);
            if (content == "hang")
            {
                await Task.Delay(Timeout.Infinite, ct);
            }

            if (content == "throw")
            {
                throw new InvalidOperationException("scripted fault");
            }

            var output = await owner._artifacts!.PutAsync(
                request.RunId, request.Stage.Id, EngineHarness.Utf8(content), request.Stage.Output, ct);
            return new StageResult(output, null, new Cost(1, TimeSpan.Zero), [new Decision("style", content, "scripted", 1)]);
        }
    }
}

/// <summary>An agent loop driven by the test: each run calls the script with the tools it was given.</summary>
internal sealed class ScriptedAgent : IDiscoveryAgent
{
    public Func<ArtifactRef, IAgentTools, Task> Run { get; set; } = (_, _) => Task.CompletedTask;

    public Func<StageRequest, IAgentTools, Task> RunStage { get; set; } = async (request, tools) =>
        await tools.WriteArtifact(request.Stage.Id, EngineHarness.Utf8($"orchestrated {request.Stage.Id}"), request.Stage.Output);

    public Task RunAsync(ArtifactRef input, IAgentTools tools, TraceRef trace, CancellationToken ct) => Run(input, tools);

    public Task RunStageAsync(StageRequest request, IAgentTools tools, TraceRef trace, CancellationToken ct) =>
        RunStage(request, tools);
}
