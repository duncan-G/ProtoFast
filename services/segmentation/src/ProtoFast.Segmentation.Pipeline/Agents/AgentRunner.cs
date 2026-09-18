using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using ProtoFast.Segmentation.Core.Validation;
using ProtoFast.Segmentation.Routing;

namespace ProtoFast.Segmentation.Pipeline.Agents;

/// <summary>What a validated agent call produced, or why it could not be validated.</summary>
public sealed record AgentResult<T>(T? Value, ValidationResult Validation, RoutedResponse? Response)
{
    public bool Success => Validation.Passed && Value is not null;
}

/// <summary>
/// The call-validate-repair loop every agent shares (plan §9.8).
///
/// <para>It is one class rather than a base class per agent because the loop is identical
/// everywhere and the differences — prompt, schema, checks — are parameters. Keeping it in one
/// place is also what makes the repair budget enforceable: an agent cannot accidentally get more
/// rounds than the configuration allows by implementing its own loop.</para>
///
/// <para>Agents are stateless. Each round's context comes from artifacts and the previous round's
/// error report, never from chat history — which is why the MAF Agent Harness is not used here
/// (plan §13.2): its compaction and long-session handling work against fixed, controlled
/// per-call context.</para>
/// </summary>
public sealed class AgentRunner(
    IRoutingChatClient router,
    PromptAssets assets,
    ILogger<AgentRunner> logger)
{
    public PromptAssets Assets => assets;

    /// <summary>
    /// Calls the model, parses, validates, and repairs up to <paramref name="maxRounds"/> times.
    /// The repair prompt carries the previous answer and the exact error — a model asked to "try
    /// again" without being told what was wrong tends to return the same thing.
    /// </summary>
    public Task<AgentResult<T>> RunAsync<T>(
        string prompt,
        RoutingContext context,
        Func<T, ValidationResult> validate,
        int maxRounds,
        Func<string, string, string>? buildRepairPrompt = null,
        CancellationToken ct = default) =>
        RunCoreAsync(
            [new ChatMessage(ChatRole.User, prompt)],
            context,
            validate,
            maxRounds,
            // Replace, not append. A stateless agent's repair prompt is a whole prompt — it
            // already carries the previous answer and the error — so appending it to the turn it
            // repairs would send the artifact twice and the skeleton twice with it.
            (_, reply, errors) =>
            [
                new ChatMessage(
                    ChatRole.User,
                    buildRepairPrompt is null
                        ? prompt + "\n\n## What failed\n" + errors
                        : buildRepairPrompt(reply, errors)),
            ],
            ct);

    /// <summary>
    /// The same loop over a conversation rather than a single prompt, for the one agent that has
    /// one: the structure orchestrator, whose input is the rounds that came before it
    /// (orchestrator plan §4.2).
    ///
    /// <para>The repair appends a user turn here rather than replacing one, because the
    /// conversation is the context that makes "here is what was wrong with your last answer"
    /// actionable for an agent whose answer was about the whole conversation. Every other agent
    /// keeps the single-prompt form and stays stateless — adding the overload is what stops the
    /// orchestration from needing its own loop, and with it its own, unenforced, repair budget.</para>
    /// </summary>
    public Task<AgentResult<T>> RunAsync<T>(
        IReadOnlyList<ChatMessage> messages,
        RoutingContext context,
        Func<T, ValidationResult> validate,
        int maxRounds,
        CancellationToken ct = default) =>
        RunCoreAsync(
            messages,
            context,
            validate,
            maxRounds,
            (conversation, reply, errors) =>
            [
                .. conversation,
                new ChatMessage(ChatRole.Assistant, reply),
                new ChatMessage(ChatRole.User, "## What failed\n" + errors),
            ],
            ct);

    private async Task<AgentResult<T>> RunCoreAsync<T>(
        IReadOnlyList<ChatMessage> messages,
        RoutingContext context,
        Func<T, ValidationResult> validate,
        int maxRounds,
        Func<IReadOnlyList<ChatMessage>, string, string, List<ChatMessage>> nextTurn,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(messages);

        var conversation = new List<ChatMessage>(messages);
        var currentContext = context;
        ValidationResult lastValidation = ValidationResult.Fail("schema", "No attempt was made.");
        RoutedResponse? lastResponse = null;

        for (var round = 0; round <= maxRounds; round++)
        {
            lastResponse = await router.GetResponseAsync(conversation, currentContext, ct);

            if (lastResponse.Truncated)
            {
                // Unfinished, not wrong. The repair path is the wrong tool here: it would show the
                // model a half-written artifact and ask it to fix the truncation, in a prompt
                // longer than the one that already did not fit — and the repair reply would stop
                // in the same place. Raise the ceiling and ask the original question again.
                lastValidation = ValidationResult.Fail(
                    Checks.Schema,
                    $"The reply stopped at the {currentContext.MaxOutputTokens}-token output cap before "
                    + "the artifact was complete.");

                if (round == maxRounds)
                {
                    break;
                }

                currentContext = currentContext with { MaxOutputTokens = currentContext.MaxOutputTokens * 2 };
                logger.LogWarning(
                    "Agent {Role} round {Round} was truncated for run {RunId}; retrying with a "
                    + "{Cap}-token cap.",
                    currentContext.Role, round + 1, currentContext.RunId, currentContext.MaxOutputTokens);
                continue;
            }

            var parsed = ModelJson.Parse<T>(lastResponse.Text);
            if (!parsed.Success)
            {
                lastValidation = ValidationResult.Fail(Checks.Schema, parsed.Error!);
            }
            else
            {
                lastValidation = validate(parsed.Value!);
                if (lastValidation.Passed)
                {
                    return new AgentResult<T>(parsed.Value, lastValidation, lastResponse);
                }
            }

            if (round == maxRounds)
            {
                break;
            }

            logger.LogInformation(
                "Agent {Role} round {Round} failed check '{Check}' for run {RunId}; repairing.",
                context.Role, round + 1, lastValidation.CheckId, context.RunId);

            conversation = nextTurn(conversation, lastResponse.Text, lastValidation.ErrorReport);
        }

        return new AgentResult<T>(default, lastValidation, lastResponse);
    }
}
