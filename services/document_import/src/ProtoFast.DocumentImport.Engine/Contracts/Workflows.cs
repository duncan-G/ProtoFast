namespace ProtoFast.DocumentImport.Engine;

/// <summary>A versioned DAG of stages.</summary>
public sealed record WorkflowDefinition(WorkflowRef Ref, IReadOnlyList<StageDefinition> Stages)
{
    /// <summary>Stages no other stage depends on. Their outputs are the run's output.</summary>
    public IReadOnlyList<StageDefinition> TerminalStages =>
        Stages.Where(s => !Stages.Any(o => o.DependsOn.Contains(s.Id))).ToList();
}

/// <summary>A stage is a contract, not an implementation.</summary>
public sealed record StageDefinition(
    string Id,
    IReadOnlyList<string> DependsOn,          // stage ids; empty = root, receives the run input
    ContractRef Input,                        // schema the input artifact must satisfy
    ContractRef Output,                       // schema the output artifact must satisfy
    IReadOnlyList<string> Verifiers,          // verifier ids; deterministic ones run first
    Budget Budget);

/// <summary>
/// The policy key a run's input classifies to. Its taxonomy is data; the engine treats
/// <see cref="Bucket"/> as opaque.
/// </summary>
public sealed record Signature(string Bucket, IReadOnlyDictionary<string, string> Facets);

public interface IClassifier
{
    Task<Signature> ClassifyAsync(ArtifactRef input, CancellationToken ct);
}
