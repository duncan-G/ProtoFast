using Microsoft.Extensions.AI;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Routing;
using RoutingContext = ProtoFast.Segmentation.Routing.RoutingContext;

namespace ProtoFast.Segmentation.IntegrationTests;

/// <summary>
/// An <see cref="IRoutingChatClient"/> that answers from a script instead of a provider.
///
/// <para>The orchestration is a protocol between agents, and a protocol is exactly the thing that
/// should be testable without a network. Scripting the replies per role lets a test say "the
/// orchestrator asks a question, then assembles" and assert what the loop did with it — including
/// the cases a recording could never produce on demand, like a model that never stops asking.</para>
/// </summary>
internal sealed class ScriptedRouter : IRoutingChatClient
{
    private readonly Dictionary<AgentRole, Queue<string>> _script = [];
    private readonly Lock _gate = new();

    public List<(AgentRole Role, string? Unit)> Calls { get; } = [];

    public ScriptedRouter Reply(AgentRole role, params string[] replies)
    {
        if (!_script.TryGetValue(role, out var queue))
        {
            queue = new Queue<string>();
            _script[role] = queue;
        }

        foreach (var reply in replies)
        {
            queue.Enqueue(reply);
        }

        return this;
    }

    public int CallsFor(AgentRole role) => Calls.Count(c => c.Role == role);

    public Task<RoutedResponse> GetResponseAsync(
        IReadOnlyList<ChatMessage> messages,
        RoutingContext context,
        CancellationToken ct = default)
    {
        string text;

        lock (_gate)
        {
            Calls.Add((context.Role, context.Unit));

            if (!_script.TryGetValue(context.Role, out var queue) || queue.Count == 0)
            {
                throw new InvalidOperationException(
                    $"The script has no reply left for {context.Role} (call {Calls.Count}).");
            }

            // The last scripted reply repeats. A test about the round cap should not have to
            // enumerate every round the cap allows.
            text = queue.Count == 1 ? queue.Peek() : queue.Dequeue();
        }

        var model = new ModelDescriptor
        {
            Key = $"test/{context.Tier.ToString().ToLowerInvariant()}",
            Provider = "test",
            ModelName = "scripted",
            Tier = context.Tier,
        };

        return Task.FromResult(new RoutedResponse(
            text,
            new RoutingDecision(model, Score: 1, WasPinned: context.PinnedModelKey is not null, SwitchedFromPinned: false),
            InputTokens: 100,
            OutputTokens: 100,
            CachedInputTokens: 0,
            CostUsd: 0,
            LatencyMs: 1));
    }

    public Task<IReadOnlyList<ModelStatus>> GetStatusAsync(CancellationToken ct = default) =>
        Task.FromResult<IReadOnlyList<ModelStatus>>([]);
}
