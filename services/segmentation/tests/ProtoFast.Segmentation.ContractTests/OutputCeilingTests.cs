using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging.Abstractions;
using ProtoFast.Segmentation.Core.Model;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Pipeline.Agents;
using ProtoFast.Segmentation.Routing;

// Microsoft.Extensions.AI ships a RoutingContext and a RoutingChatClient of its own; these
// are the pipeline's.
using RoutingContext = ProtoFast.Segmentation.Routing.RoutingContext;
using RoutingChatClient = ProtoFast.Segmentation.Routing.RoutingChatClient;

namespace ProtoFast.Segmentation.ContractTests;

/// <summary>
/// Output caps and the truncation they cause.
///
/// <para>A reasoning model spends its thinking against the same cap as its answer, so a cap sized
/// for the artifact alone is spent before the artifact starts and the reply comes back cut off
/// mid-token. That reply is unfinished, not malformed — and the repair loop, which exists for
/// malformed ones, makes it worse: it re-asks in a longer prompt under the same cap. These tests
/// pin both halves of the fix.</para>
/// </summary>
public class OutputCeilingTests
{
    private static RoutingContext Context(int maxOutputTokens) =>
        new(
            "run-1", "doc-1", PipelinePhase.InferStructure, AgentRole.Structurer,
            ModelTier.Large, Sensitivity.Internal,
            EstimatedInputTokens: 4_000,
            MaxOutputTokens: maxOutputTokens)
        {
            PromptVersion = "v1",
        };

    private static ModelDescriptor Model(int reserve, int max = 65_536) =>
        new()
        {
            Key = "gemini/gemini-pro",
            Provider = "gemini",
            ModelName = "gemini-3.1-pro-preview",
            ContextTokens = 1_048_576,
            MaxOutputTokens = max,
            ReservedReasoningTokens = reserve,
        };

    [Fact]
    public void TheCeilingAddsTheModelsReasoningReserveToWhatTheAgentAskedFor()
    {
        // The structurer asks for 2048 because that is what the tree costs. Gemini Pro spends
        // thousands thinking first, and the provider counts that against the same cap.
        Assert.Equal(2_048 + 8_192, RoutingChatClient.Ceiling(Model(reserve: 8_192), Context(2_048)));
    }

    [Fact]
    public void AModelThatDoesNotReasonGetsExactlyWhatWasAsked()
    {
        Assert.Equal(2_048, RoutingChatClient.Ceiling(Model(reserve: 0), Context(2_048)));
    }

    [Fact]
    public void TheCeilingNeverExceedsWhatTheModelAccepts()
    {
        Assert.Equal(8_192, RoutingChatClient.Ceiling(Model(reserve: 8_192, max: 8_192), Context(4_096)));
    }

    [Fact]
    public async Task ATruncatedReplyIsRetriedWithADoubledCapRatherThanRepaired()
    {
        var router = new FakeRouter(
            new Reply("""{"ok":true}""", Truncated: true),
            new Reply("""{"ok":true}""", Truncated: false));

        var result = await new AgentRunner(router, new PromptAssets(), NullLogger<AgentRunner>.Instance)
            .RunAsync<Artifact>(
                "structure this",
                Context(2_048),
                _ => ValidationResult.Pass("schema"),
                maxRounds: 2,
                buildRepairPrompt: (_, _) => "REPAIR",
                TestContext.Current.CancellationToken);

        Assert.True(result.Success);

        // Two calls: the truncated one and the retry. The retry asks the ORIGINAL question — a
        // repair prompt here would be asking the model to fix an answer it never finished writing.
        Assert.Equal(2, router.Calls.Count);
        Assert.All(router.Calls, call => Assert.Equal("structure this", call.Prompt));
        Assert.Equal(2_048, router.Calls[0].Context.MaxOutputTokens);
        Assert.Equal(4_096, router.Calls[1].Context.MaxOutputTokens);
    }

    [Fact]
    public async Task TruncationThatOutlastsTheRoundsFailsSayingSoRatherThanBlamingTheSchema()
    {
        var router = new FakeRouter(
            new Reply("{", Truncated: true),
            new Reply("{", Truncated: true));

        var result = await new AgentRunner(router, new PromptAssets(), NullLogger<AgentRunner>.Instance)
            .RunAsync<Artifact>(
                "structure this",
                Context(2_048),
                _ => ValidationResult.Pass("schema"),
                maxRounds: 1,
                buildRepairPrompt: (_, _) => "REPAIR",
                TestContext.Current.CancellationToken);

        Assert.False(result.Success);

        // The operator reading this needs to know the cap ran out, not that the model writes bad
        // JSON — the two point at different fixes.
        Assert.Contains("output cap", result.Validation.ErrorReport);
    }

    private sealed record Artifact(bool Ok);

    private sealed record Reply(string Text, bool Truncated);

    private sealed record Call(string Prompt, RoutingContext Context);

    private sealed class FakeRouter(params Reply[] replies) : IRoutingChatClient
    {
        public List<Call> Calls { get; } = [];

        public Task<RoutedResponse> GetResponseAsync(
            IReadOnlyList<ChatMessage> messages, RoutingContext context, CancellationToken ct = default)
        {
            var reply = replies[Math.Min(Calls.Count, replies.Length - 1)];
            Calls.Add(new Call(messages[^1].Text, context));

            return Task.FromResult(new RoutedResponse(
                reply.Text,
                new RoutingDecision(Model(8_192), 1.0, true, false),
                InputTokens: 4_000, OutputTokens: 10, CachedInputTokens: 0,
                CostUsd: 0m, LatencyMs: 1)
            {
                Truncated = reply.Truncated,
            });
        }

        public Task<IReadOnlyList<ModelStatus>> GetStatusAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ModelStatus>>([]);
    }
}
