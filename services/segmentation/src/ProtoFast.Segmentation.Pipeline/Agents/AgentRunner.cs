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
    public async Task<AgentResult<T>> RunAsync<T>(
        string prompt,
        RoutingContext context,
        Func<T, ValidationResult> validate,
        int maxRounds,
        Func<string, string, string>? buildRepairPrompt = null,
        CancellationToken ct = default)
    {
        var currentPrompt = prompt;
        ValidationResult lastValidation = ValidationResult.Fail("schema", "No attempt was made.");
        RoutedResponse? lastResponse = null;

        for (var round = 0; round <= maxRounds; round++)
        {
            lastResponse = await router.GetResponseAsync(
                [new ChatMessage(ChatRole.User, currentPrompt)], context, ct);

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

            currentPrompt = buildRepairPrompt is null
                ? prompt + "\n\n## What failed\n" + lastValidation.ErrorReport
                : buildRepairPrompt(lastResponse.Text, lastValidation.ErrorReport);
        }

        return new AgentResult<T>(default, lastValidation, lastResponse);
    }
}
