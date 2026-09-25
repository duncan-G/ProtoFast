namespace ProtoFast.DocumentImport.Engine.InMemory;

/// <summary>
/// Versions per id, assigned on publish. Applies the human gate on publish: a seed keeps the
/// <see cref="ExecutorSpec.Promoted"/> it was configured with, an agent-defined executor is
/// promoted unless it carries code, and a distilled one never is.
/// </summary>
public sealed class InMemoryRegistry : IRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<string, List<Playbook>> _playbooks = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<ExecutorSpec>> _executors = new(StringComparer.Ordinal);
    private readonly Dictionary<string, List<WorkflowDefinition>> _workflows = new(StringComparer.Ordinal);
    private readonly HashSet<WorkflowRef> _promotedWorkflows = [];

    public Task<Playbook> ResolveAsync(PlaybookRef reference, CancellationToken ct) =>
        Task.FromResult(Find(_playbooks, reference.Id, reference.Version, $"Playbook {reference.Id}@{reference.Version}"));

    public Task<ExecutorSpec> ResolveAsync(ExecutorRef reference, CancellationToken ct) =>
        Task.FromResult(Find(_executors, reference.Id, reference.Version, $"Executor {reference}"));

    public Task<WorkflowDefinition> ResolveAsync(WorkflowRef reference, CancellationToken ct) =>
        Task.FromResult(Find(_workflows, reference.Id, reference.Version, $"Workflow {reference.Id}@{reference.Version}"));

    public Task<PlaybookRef> PublishAsync(Playbook playbook, CancellationToken ct)
    {
        lock (_gate)
        {
            var versions = VersionsOf(_playbooks, playbook.Ref.Id);
            var reference = new PlaybookRef(playbook.Ref.Id, versions.Count + 1);
            versions.Add(playbook with { Ref = reference });
            return Task.FromResult(reference);
        }
    }

    public Task<ExecutorRef> PublishAsync(ExecutorSpec spec, CancellationToken ct)
    {
        lock (_gate)
        {
            var versions = VersionsOf(_executors, spec.Ref.Id);
            var reference = new ExecutorRef(spec.Ref.Id, versions.Count + 1);
            var promoted = spec.Origin switch
            {
                ExecutorOrigin.Seed => spec.Promoted,
                ExecutorOrigin.AgentDefined => spec.CodeAssembly is null && spec.Tier != Tier.Codified,
                _ => false,
            };
            versions.Add(spec with { Ref = reference, Promoted = promoted });
            return Task.FromResult(reference);
        }
    }

    public Task<WorkflowRef> PublishAsync(WorkflowDefinition workflow, CancellationToken ct)
    {
        lock (_gate)
        {
            var versions = VersionsOf(_workflows, workflow.Ref.Id);
            var reference = new WorkflowRef(workflow.Ref.Id, versions.Count + 1);
            versions.Add(workflow with { Ref = reference });
            return Task.FromResult(reference);
        }
    }

    public Task PromoteAsync(ExecutorRef reference, CancellationToken ct)
    {
        lock (_gate)
        {
            var versions = VersionsOf(_executors, reference.Id);
            var index = reference.Version - 1;
            if (index < 0 || index >= versions.Count)
            {
                throw new KeyNotFoundException($"Executor {reference} does not exist.");
            }

            versions[index] = versions[index] with { Promoted = true };
        }

        return Task.CompletedTask;
    }

    public Task PromoteAsync(WorkflowRef reference, CancellationToken ct)
    {
        lock (_gate)
        {
            Find(_workflows, reference.Id, reference.Version, $"Workflow {reference.Id}@{reference.Version}");
            _promotedWorkflows.Add(reference);
        }

        return Task.CompletedTask;
    }

    public Task<bool> IsPromotedAsync(WorkflowRef reference, CancellationToken ct)
    {
        lock (_gate)
        {
            return Task.FromResult(_promotedWorkflows.Contains(reference));
        }
    }

    private static List<T> VersionsOf<T>(Dictionary<string, List<T>> items, string id)
    {
        if (!items.TryGetValue(id, out var versions))
        {
            versions = [];
            items[id] = versions;
        }

        return versions;
    }

    private T Find<T>(Dictionary<string, List<T>> items, string id, int version, string description)
    {
        lock (_gate)
        {
            return items.TryGetValue(id, out var versions) && version >= 1 && version <= versions.Count
                ? versions[version - 1]
                : throw new KeyNotFoundException($"{description} does not exist.");
        }
    }
}
