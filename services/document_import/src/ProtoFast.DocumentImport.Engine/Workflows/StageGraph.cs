using System.Runtime.ExceptionServices;
using ProtoFast.DocumentImport.Engine.Storage;

namespace ProtoFast.DocumentImport.Engine.Workflows;

public static class StageGraph
{
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

    /// <summary>Null when the stages contain a cycle.</summary>
    public static IReadOnlyList<StageDefinition>? TopologicalOrder(IReadOnlyList<StageDefinition> stages)
    {
        var byId = stages.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var remaining = stages.ToDictionary(
            s => s.Id, s => s.DependsOn.Count(byId.ContainsKey), StringComparer.Ordinal);

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

    /// <summary>Rethrows the first failure, not the cancellations it triggers downstream.</summary>
    public static async Task RunDagAsync(
        this IReadOnlyList<StageDefinition> stages, Action onError, Func<StageDefinition, Task> body)
    {
        var byId = stages.ToDictionary(s => s.Id, StringComparer.Ordinal);
        var tasks = new Dictionary<string, Task>(StringComparer.Ordinal);

        Task Start(StageDefinition stage)
        {
            if (!tasks.TryGetValue(stage.Id, out var task))
            {
                var dependencies = stage.DependsOn.Select(id => Start(byId[id])).ToList();
                task = RunStageAsync(stage, dependencies);
                tasks[stage.Id] = task;
            }

            return task;
        }

        async Task RunStageAsync(StageDefinition stage, List<Task> dependencies)
        {
            await Task.WhenAll(dependencies);

            // Yield so root stages start concurrently.
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
