using ProtoFast.DocumentImport.Engine.Verification;
using ProtoFast.DocumentImport.Engine.Workflows;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

/// <summary>
/// What a discovery run must deliver: the one stage the caller reads, its contract and the verifiers
/// that judge it. Everything before that stage is the agent's to discover.
/// </summary>
/// <param name="Brief">Markdown for the agent: the deliverable's shape and rules.</param>
public sealed record DiscoveryGoal(
    string StageId, ContractRef Contract, IReadOnlyList<VerifierSpec> Verifiers, string Brief);
