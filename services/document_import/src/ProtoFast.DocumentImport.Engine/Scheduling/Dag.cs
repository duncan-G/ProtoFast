using System.Runtime.ExceptionServices;

namespace ProtoFast.DocumentImport.Engine;

public static class Dag
{
    /// <summary>
    /// Rejects a workflow the scheduler cannot walk: no stages, a duplicate or reserved id, a
    /// dependency on a stage that does not exist, or a cycle.
    /// </summary>
    public static void Validate(WorkflowDefinition workflow)
    {
        if (workflow.Stages.Count == 0)
        {
            throw new InvalidWorkflowException(workflow.Ref, "it has no stages");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var stage in workflow.Stages)
        {
            if (stage.Id == ArtifactRef.InputStageId)
            {
                throw new InvalidWorkflowException(workflow.Ref, $"'{ArtifactRef.InputStageId}' is reserved for the run input");
            }

            if (!ids.Add(stage.Id))
            {
                throw new InvalidWorkflowException(workflow.Ref, $"stage '{stage.Id}' is defined twice");
            }
        }

        foreach (var stage in workflow.Stages)
        {
            foreach (var dependency in stage.DependsOn)
            {
                if (!ids.Contains(dependency))
                {
                    throw new InvalidWorkflowException(workflow.Ref, $"stage '{stage.Id}' depends on unknown stage '{dependency}'");
                }
            }
        }

        if (TopologicalOrder(workflow.Stages) is null)
        {
            throw new InvalidWorkflowException(workflow.Ref, "its dependencies form a cycle");
        }
    }

    /// <summary>Stages ordered so every stage follows its dependencies, or null when there is a cycle.</summary>
    public static IReadOnlyList<StageDefinition>? TopologicalOrder(IReadOnlyList<StageDefinition> stages)
    {
        var byId = stages.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var remaining = stages.ToDictionary(
            s => s.Id, s => s.DependsOn.Count(byId.ContainsKey), StringComparer.Ordinal);

        // Kahn's algorithm, seeded in declaration order so the result is deterministic.
        var ready = new Queue<StageDefinition>(stages.Where(s => remaining[s.Id] == 0));
        var ordered = new List<StageDefinition>(stages.Count);
        while (ready.TryDequeue(out var stage))
        {
            ordered.Add(stage);
            foreach (var dependent in stages.Where(s => s.DependsOn.Contains(stage.Id)))
            {
                if (--remaining[dependent.Id] == 0)
                {
                    ready.Enqueue(dependent);
                }
            }
        }

        return ordered.Count == stages.Count ? ordered : null;
    }

    /// <summary>
    /// Runs <paramref name="body"/> for every stage as soon as all of its dependencies have
    /// completed, so independent stages run concurrently. The first failure calls
    /// <paramref name="onError"/> (the scheduler cancels the run there) and is what this rethrows,
    /// not the cancellations it causes downstream.
    /// </summary>
    public static async Task RunDagAsync(
        this IReadOnlyList<StageDefinition> stages, Action onError, Func<StageDefinition, Task> body)
    {
        var byId = stages.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var tasks = new Dictionary<string, Task>(StringComparer.Ordinal);

        Task Start(StageDefinition stage)
        {
            if (!tasks.TryGetValue(stage.Id, out var task))
            {
                // Validation has ruled out cycles, so this recursion terminates, and every
                // dependency's task exists before its dependent's.
                var dependencies = stage.DependsOn.Select(id => Start(byId[id])).ToList();
                task = RunStageAsync(stage, dependencies);
                tasks[stage.Id] = task;
            }

            return task;
        }

        async Task RunStageAsync(StageDefinition stage, List<Task> dependencies)
        {
            await Task.WhenAll(dependencies);

            // Leave the caller's synchronous start-up loop before doing any work, so roots start together.
            await Task.Yield();
            try
            {
                await body(stage);
            }
            catch
            {
                onError();
                throw;
            }
        }

        foreach (var stage in stages)
        {
            _ = Start(stage);
        }

        try
        {
            await Task.WhenAll(tasks.Values);
        }
        catch
        {
            var root = tasks.Values
                .Where(t => t.IsFaulted)
                .SelectMany(t => t.Exception!.InnerExceptions)
                .FirstOrDefault(e => e is not OperationCanceledException);
            if (root is not null)
            {
                ExceptionDispatchInfo.Throw(root);
            }

            throw;
        }
    }
}

public sealed class InvalidWorkflowException(WorkflowRef workflow, string reason)
    : Exception($"Workflow {workflow.Id}@{workflow.Version} is invalid: {reason}.")
{
    public WorkflowRef Workflow { get; } = workflow;
}
