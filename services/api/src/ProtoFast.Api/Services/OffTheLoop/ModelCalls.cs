using System.Runtime.CompilerServices;
using Microsoft.Extensions.Options;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// Runs each attempt at a model call in its own GenAI chat span, and waits out a provider outage with
/// exponential backoff until the reply starts streaming; a retry after that would repeat what the caller has.
/// </summary>
public sealed class ModelCalls(IOptions<ModelProviders> providers, TimeProvider time, ILogger<ModelCalls> logger)
{
    /// <param name="call">One attempt, which finishes the span it is given once the reply is in.</param>
    public async IAsyncEnumerable<T> StreamAsync<T>(
        ChatRequest request, Func<ChatSpan, IAsyncEnumerable<T>> call, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var (span, items, more) = await OpenAsync(request, call, cancellationToken);
        try
        {
            while (more)
            {
                yield return items.Current;
                more = await NextAsync(span, items, cancellationToken);
            }
        }
        finally
        {
            await items.DisposeAsync();
            span.Dispose();
        }
    }

    private async Task<(ChatSpan Span, IAsyncEnumerator<T> Items, bool More)> OpenAsync<T>(
        ChatRequest request, Func<ChatSpan, IAsyncEnumerable<T>> call, CancellationToken cancellationToken)
    {
        var retry = providers.Value.Retry;
        var waited = TimeSpan.Zero;
        var delay = retry.InitialDelay;
        for (var attempt = 1; ; attempt++)
        {
            var span = ChatSpan.Start(request, providers.Value.CaptureMessageContent);
            var items = call(span).GetAsyncEnumerator(cancellationToken);
            try
            {
                return (span, items, await NextAsync(span, items, cancellationToken));
            }
            catch (Exception e)
            {
                await items.DisposeAsync();
                span.Dispose();
                if (!TransientModelErrors.IsTransient(e, cancellationToken) || waited + delay > retry.MaxWait)
                {
                    throw;
                }

                logger.LogWarning(e, "{Model} is unavailable (attempt {Attempt}); retrying in {Delay}", request.Model, attempt, delay);
            }

            await Task.Delay(delay, time, cancellationToken);
            waited += delay;
            delay = delay * 2 > retry.MaxDelay ? retry.MaxDelay : delay * 2;
        }
    }

    private static async Task<bool> NextAsync<T>(ChatSpan span, IAsyncEnumerator<T> items, CancellationToken cancellationToken)
    {
        try
        {
            return await items.MoveNextAsync();
        }
        catch (Exception e) when (!cancellationToken.IsCancellationRequested)
        {
            span.Fail(e);
            throw;
        }
    }
}
