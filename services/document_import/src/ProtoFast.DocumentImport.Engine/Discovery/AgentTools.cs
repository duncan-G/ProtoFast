using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Engine.Learning;
using ProtoFast.DocumentImport.Engine.Policy;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Engine.Discovery;

public sealed class AgentTools : IAgentTools
{
    private readonly AgentToolsFactory _engine;
    private readonly string _runId;
    private readonly Signature _signature;
    private readonly ArtifactRef _input;
    private readonly TraceRef _trace;
    private readonly StageRequest? _scope;
    private readonly CancellationToken _ct;

    private readonly ConcurrentDictionary<string, ContractRef> _contracts = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<Decision> _scopedDecisions = new();

    internal AgentTools(
        AgentToolsFactory engine, string runId, Signature signature, ArtifactRef input, TraceRef trace,
        StageRequest? scope, CancellationToken ct)
    {
        _engine = engine;
        _runId = runId;
        _signature = signature;
        _input = input;
        _trace = trace;
        _scope = scope;
        _ct = ct;
    }

    private string Family => _signature.Family;

    /// <summary>Stage scope only.</summary>
    public ArtifactRef? ScopedOutput { get; private set; }

    /// <summary>Stage scope only.</summary>
    public IReadOnlyList<Decision> ScopedDecisions => _scopedDecisions.ToList();

    public async Task<DocumentFamilyContext> Context()
    {
        var runs = await _engine.Ledger.RecentAsync(Family, RunMode.Discovery, _engine.Options.ContextRuns, _ct);
        var verifiers = await _engine.Catalog.VerifiersAsync(Family, _ct);
        var records = runs.SelectMany(r => r.Stages).ToList();

        var stageIds = records.Select(r => r.StageId).Concat(verifiers.Select(v => v.StageId)).Distinct().ToList();

        var executorRefs = (await _engine.Catalog.ExecutorsAsync(Family, _ct))
            .Concat(records.Where(r => r.Tier != Tier.Orchestrator).Select(r => r.Executor))
            .Distinct()
            .ToList();
        var executors = new List<ExecutorSpec>(executorRefs.Count);
        foreach (var executor in executorRefs)
        {
            try
            {
                executors.Add(await _engine.Registry.ResolveAsync(executor, _ct));
            }
            catch (KeyNotFoundException)
            {
                // A record can outlive its registry entry.
            }
        }

        if (_scope is { } scope)
        {
            stageIds = [scope.Stage.Id];
            verifiers = verifiers.Where(v => v.StageId == scope.Stage.Id).ToList();
        }

        return new DocumentFamilyContext(stageIds, executors, verifiers);
    }

    public Task<Stream> ReadArtifact(ArtifactRef reference) => _engine.Artifacts.GetAsync(reference, _ct);

    public async Task<WriteResult> WriteArtifact(
        string stageId, Stream content, ContractRef contract, IReadOnlyList<ArtifactRef>? inputs = null)
    {
        EnsureWritable(stageId);
        inputs ??= _scope?.Inputs ?? [];
        EnsureInputsBelongToRun(inputs);

        if (_scope is { } scope)
        {
            if (contract != scope.Stage.Output)
            {
                throw new ArgumentException(
                    $"Stage '{stageId}' must produce {scope.Stage.Output}, not {contract}.", nameof(contract));
            }

            var scopedOutput = await _engine.Artifacts.PutAsync(_runId, stageId, content, contract, _ct);
            var scopedVerdicts = await _engine.Verifiers.RunAsync(
                scope, new StageResult(scopedOutput, _trace, Cost.Zero, []), Tier.Orchestrator, _ct);
            ScopedOutput = scopedOutput;
            return new WriteResult(scopedOutput, scopedVerdicts);
        }

        var output = await _engine.Artifacts.PutAsync(_runId, stageId, content, contract, _ct);
        _contracts[stageId] = contract;

        var stage = await RecordedStageAsync(stageId, inputs, contract);
        var request = new StageRequest(_runId, stage, _signature, inputs);
        var result = new StageResult(output, _trace, Cost.Zero, []);
        var verdicts = await _engine.Verifiers.RunAsync(request, result, Tier.Orchestrator, _ct);

        var record = new StageRecord(
            _runId, stage, inputs, _engine.Options.Orchestrator, Tier.Orchestrator, result, verdicts, IsShadow: false);
        await _engine.Ledger.RecordAsync(record, _ct);
        await _engine.Outcomes.PublishAsync(Outcome.From(record, Family, _engine.Time.GetUtcNow()), _ct);

        return new WriteResult(output, verdicts);
    }

    public async Task<PlaybookRef> DefinePlaybook(Playbook playbook)
    {
        if (string.IsNullOrWhiteSpace(playbook.Ref.Id) || string.IsNullOrWhiteSpace(playbook.Instructions))
        {
            throw new ArgumentException("A playbook needs an id and instructions.", nameof(playbook));
        }

        foreach (var example in playbook.Examples)
        {
            await EnsureArtifactExistsAsync(example.Input);
            await EnsureArtifactExistsAsync(example.Output);
        }

        return await _engine.Registry.PublishAsync(playbook, _ct);
    }

    public Task<string> UploadCode(Stream code) => _engine.Registry.PublishCodeAsync(code, _ct);

    public async Task<ExecutorRef> DefineExecutor(ExecutorSpec spec)
    {
        ValidateStructure(spec);
        if (spec.CodeAssembly is { } code && !await _engine.Registry.CodeExistsAsync(code, _ct))
        {
            throw new ArgumentException($"No uploaded code has hash {code}; call UploadCode first.", nameof(spec));
        }

        if (spec.Playbook is { } playbook)
        {
            try
            {
                await _engine.Registry.ResolveAsync(playbook, _ct);
            }
            catch (KeyNotFoundException)
            {
                throw new ArgumentException($"Playbook {playbook.Id}@{playbook.Version} does not exist.", nameof(spec));
            }
        }

        foreach (var validator in _engine.Validators)
        {
            await validator.ValidateAsync(spec, _ct);
        }

        // The registry decides Promoted.
        var published = await _engine.Registry.PublishAsync(spec with { Origin = ExecutorOrigin.AgentDefined }, _ct);
        await _engine.Catalog.AddExecutorAsync(Family, published, _ct);
        return published;
    }

    public async Task<string> DefineVerifier(VerifierSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Id) || string.IsNullOrWhiteSpace(spec.Rubric))
        {
            throw new ArgumentException("A verifier needs an id and a rubric.", nameof(spec));
        }

        EnsureWritable(spec.StageId);

        var existing = (await _engine.Catalog.VerifiersAsync(Family, _ct)).FirstOrDefault(v => v.Id == spec.Id);
        if (existing is not null)
        {
            // A different spec under the same id would change what earlier records were judged against.
            return existing == spec
                ? spec.Id
                : throw new ArgumentException($"Verifier '{spec.Id}' is already defined differently in this document family.", nameof(spec));
        }

        await _engine.Catalog.AddVerifierAsync(Family, spec, _ct);
        return spec.Id;
    }

    public async Task<StageRecord> Delegate(
        string stageId, ExecutorRef executor, IReadOnlyList<ArtifactRef> inputs, ContractRef? output = null)
    {
        EnsureWritable(stageId);
        EnsureInputsBelongToRun(inputs);

        var spec = await _engine.Registry.ResolveAsync(executor, _ct);
        if (spec.Tier == Tier.Orchestrator)
        {
            throw new ArgumentException("The agent loop is the Orchestrator; delegate to a lower tier.", nameof(executor));
        }

        if (spec.Origin == ExecutorOrigin.AgentDefined
            && !(await _engine.Catalog.ExecutorsAsync(Family, _ct)).Contains(executor))
        {
            throw new ArgumentException($"Executor {executor} was defined in another document family.", nameof(executor));
        }

        StageDefinition stage;
        if (_scope is { } scope)
        {
            if (output is { } requested && requested != scope.Stage.Output)
            {
                throw new ArgumentException($"Stage '{stageId}' must produce {scope.Stage.Output}.", nameof(output));
            }

            stage = scope.Stage;
        }
        else
        {
            var contract = output
                ?? (_contracts.TryGetValue(stageId, out var known) ? known : await LastRecordedContractAsync(stageId))
                ?? throw new ArgumentException(
                    $"Stage '{stageId}' has no recorded output contract in this document family; pass one.", nameof(output));
            _contracts[stageId] = contract;
            stage = await RecordedStageAsync(stageId, inputs, contract);
        }

        await Record(new Decision($"executor:{stageId}", executor.ToString(), "Delegated by the agent loop.", 1));

        var record = await _engine.Attempts.RunAsync(
            new StageRequest(_runId, stage, _signature, inputs), executor, isShadow: false, _ct);

        if (_scope is not null && record.Passed)
        {
            ScopedOutput = record.Output;
        }

        return record;
    }

    public Task Record(Decision decision)
    {
        if (_scope is not null)
        {
            _scopedDecisions.Enqueue(decision);
            return Task.CompletedTask;
        }

        return _engine.Ledger.RecordAsync(_runId, decision, _ct);
    }

    private void ValidateStructure(ExecutorSpec spec)
    {
        if (string.IsNullOrWhiteSpace(spec.Ref.Id))
        {
            throw new ArgumentException("An executor needs an id.", nameof(spec));
        }

        switch (spec.Tier)
        {
            case Tier.Orchestrator:
                throw new ArgumentException("The agent loop is the only Orchestrator executor.", nameof(spec));

            case Tier.Codified:
                if (string.IsNullOrWhiteSpace(spec.CodeAssembly)
                    || spec.ModelClass is not null || spec.Playbook is not null || spec.Tools.Count > 0)
                {
                    throw new ArgumentException(
                        "A Codified executor has an assembly and no model class, playbook or tools.", nameof(spec));
                }

                break;

            default:
                if (spec.ModelClass != ModelClasses.For(spec.Tier) || spec.Playbook is null || spec.CodeAssembly is not null)
                {
                    throw new ArgumentException(
                        $"A {spec.Tier} executor runs on the '{ModelClasses.For(spec.Tier)}' model class with a playbook and no assembly.",
                        nameof(spec));
                }

                if (_engine.Options.AgentToolNames is { } allowed && spec.Tools.FirstOrDefault(t => !allowed.Contains(t)) is { } tool)
                {
                    throw new ArgumentException($"Tool '{tool}' is not available to executors.", nameof(spec));
                }

                break;
        }
    }

    private void EnsureWritable(string stageId)
    {
        if (string.IsNullOrWhiteSpace(stageId) || stageId == ArtifactRef.InputStageId)
        {
            throw new ArgumentException($"'{stageId}' is not a usable stage id.", nameof(stageId));
        }

        if (_scope is { } scope && stageId != scope.Stage.Id)
        {
            throw new ArgumentException($"This loop can only write stage '{scope.Stage.Id}'.", nameof(stageId));
        }
    }

    private async Task EnsureArtifactExistsAsync(ArtifactRef reference)
    {
        try
        {
            await _engine.Artifacts.ContractOfAsync(reference, _ct);
        }
        catch (KeyNotFoundException)
        {
            throw new ArgumentException($"Artifact {reference} does not exist.", nameof(reference));
        }
    }

    private void EnsureInputsBelongToRun(IReadOnlyList<ArtifactRef> inputs)
    {
        foreach (var input in inputs)
        {
            if (input != _input && input.RunId != _runId && _scope?.Inputs.Contains(input) != true)
            {
                throw new ArgumentException($"Artifact {input} is not the run input or an artifact of this run.", nameof(inputs));
            }
        }
    }

    private async Task<StageDefinition> RecordedStageAsync(string stageId, IReadOnlyList<ArtifactRef> inputs, ContractRef output)
    {
        var dependsOn = inputs
            .Select(i => i.StageId)
            .Where(id => id != ArtifactRef.InputStageId)
            .Distinct()
            .ToList();
        var input = await _engine.Artifacts.ContractOfAsync(inputs.Count > 0 ? inputs[0] : _input, _ct);
        var verifiers = (await _engine.Catalog.VerifiersAsync(Family, _ct))
            .Where(v => v.StageId == stageId)
            .Select(v => v.Id)
            .ToList();

        return new StageDefinition(stageId, dependsOn, input, output, verifiers, _engine.Options.DiscoveryBudget);
    }

    private async Task<ContractRef?> LastRecordedContractAsync(string stageId)
    {
        var runs = await _engine.Ledger.RecentAsync(Family, RunMode.Discovery, _engine.Options.ContextRuns, _ct);
        return runs
            .SelectMany(r => r.Stages)
            .Where(s => s.StageId == stageId)
            .Select(s => (ContractRef?)s.Stage.Output)
            .FirstOrDefault();
    }
}
