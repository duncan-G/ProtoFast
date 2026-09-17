using Microsoft.Extensions.AI;

namespace ProtoFast.Segmentation.Routing;

/// <summary>The completed call: its text, what it cost, and which model produced it.</summary>
public sealed record RoutedResponse(
    string Text,
    RoutingDecision Decision,
    int InputTokens,
    int OutputTokens,
    int CachedInputTokens,
    decimal CostUsd,
    int LatencyMs);

/// <summary>
/// The single door to every provider (plan §14.1). Executors hold this and nothing else — there
/// is no seam through which an executor could construct a provider client, which is what makes
/// the sensitivity allow-list and the budget ledger enforceable rather than advisory.
/// </summary>
public interface IRoutingChatClient
{
    Task<RoutedResponse> GetResponseAsync(
        IReadOnlyList<ChatMessage> messages,
        RoutingContext context,
        CancellationToken ct = default);

    /// <summary>Live headroom and circuit state per model, for <c>ListModels</c> (plan §17).</summary>
    Task<IReadOnlyList<ModelStatus>> GetStatusAsync(CancellationToken ct = default);
}

public sealed record ModelStatus(
    string Key,
    string Provider,
    string Tier,
    IReadOnlyList<string> QualifiedRoles,
    double HeadroomFraction,
    bool CircuitOpen);
