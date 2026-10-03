using Microsoft.Extensions.Logging;

namespace ProtoFast.DocumentImport.Screenplay.Models;

/// <summary>
/// Waits out a provider outage with exponential backoff, keeping the conversation in memory, and
/// throws <see cref="LanguageModelUnavailableException"/> once the retry window is spent.
/// </summary>
public sealed class RetryingLanguageModel(ILanguageModel inner, ModelRetryOptions options, TimeProvider time, ILogger logger)
    : ILanguageModel
{
    public string ModelId => inner.ModelId;

    public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct) =>
        RetryAsync(() => inner.CompleteAsync(system, user, ct), ct);

    public Task<LanguageModelReply> ConverseAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
        RetryAsync(() => inner.ConverseAsync(system, messages, tools, ct), ct);

    private async Task<LanguageModelReply> RetryAsync(Func<Task<LanguageModelReply>> call, CancellationToken ct)
    {
        var waited = TimeSpan.Zero;
        var delay = options.InitialDelay;
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return await call();
            }
            catch (Exception e) when (TransientModelErrors.IsTransient(e, ct))
            {
                if (waited + delay > options.MaxWait)
                {
                    throw new LanguageModelUnavailableException(inner.ModelId, attempt, waited, e);
                }

                logger.LogWarning(e, "{Model} is unavailable (attempt {Attempt}); retrying in {Delay}", inner.ModelId, attempt, delay);
                await Task.Delay(delay, time, ct);
                waited += delay;
                delay = delay * 2 > options.MaxDelay ? options.MaxDelay : delay * 2;
            }
        }
    }
}
