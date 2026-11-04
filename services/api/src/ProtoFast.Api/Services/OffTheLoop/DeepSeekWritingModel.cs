using System.Runtime.CompilerServices;
using System.Text;
using Anthropic;
using Anthropic.Models.Messages;
using Grpc.Core;
using Microsoft.Extensions.Options;
using Status = Grpc.Core.Status;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>DeepSeek, through the Anthropic Messages API it serves.</summary>
public sealed class DeepSeekWritingModel(
    IOptions<WriterOptions> options, IOptions<ModelProviders> providers, ModelCalls calls, ILogger<DeepSeekWritingModel> logger)
    : IWritingModel
{
    private const string Provider = "deepseek";

    private readonly Lazy<AnthropicClient?> _client = new(() =>
        providers.Value.DeepSeek is { ApiKey.Length: > 0 } deepSeek
            ? new AnthropicClient { ApiKey = deepSeek.ApiKey, BaseUrl = deepSeek.BaseUrl }
            : null);

    public IAsyncEnumerable<string> WriteAsync(string system, string user, CancellationToken cancellationToken)
    {
        var client = _client.Value ?? throw new RpcException(new Status(StatusCode.Unavailable, LoopErrors.WriterOff));
        var request = new ChatRequest(
            Provider, new Uri(providers.Value.DeepSeek.BaseUrl), options.Value.Model, options.Value.MaxOutputTokens, system, user);
        return calls.StreamAsync(request, span => WriteAsync(client, request, span, cancellationToken), cancellationToken);
    }

    private async IAsyncEnumerable<string> WriteAsync(
        AnthropicClient client, ChatRequest request, ChatSpan span, [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var parameters = new MessageCreateParams
        {
            Model = request.Model,
            MaxTokens = request.MaxOutputTokens,
            System = request.System,
            Messages = [new() { Role = Role.User, Content = request.User }],
            // DeepSeek thinks by default, holding back the first words ~10 s.
            Thinking = new ThinkingConfigDisabled(),
        };

        var written = new StringBuilder();
        string? responseId = null, responseModel = null, stopReason = null;
        long input = 0, cacheWrites = 0, cacheReads = 0, output = 0;
        await foreach (var streamEvent in client.Messages.CreateStreaming(parameters, cancellationToken))
        {
            if (streamEvent.TryPickContentBlockDelta(out var block) && block.Delta.TryPickText(out var text))
            {
                written.Append(text.Text);
                yield return text.Text;
            }
            else if (streamEvent.TryPickStart(out var start))
            {
                responseId = start.Message.ID;
                responseModel = start.Message.Model.Raw();
                input = start.Message.Usage.InputTokens;
                cacheWrites = start.Message.Usage.CacheCreationInputTokens ?? 0;
                cacheReads = start.Message.Usage.CacheReadInputTokens ?? 0;
            }
            else if (streamEvent.TryPickDelta(out var delta))
            {
                // Its input counts are cumulative where it gives them.
                stopReason = delta.Delta.StopReason?.Raw();
                output = delta.Usage.OutputTokens;
                input = delta.Usage.InputTokens ?? input;
                cacheWrites = delta.Usage.CacheCreationInputTokens ?? cacheWrites;
                cacheReads = delta.Usage.CacheReadInputTokens ?? cacheReads;
            }
        }

        var usage = new ModelUsage(input + cacheWrites + cacheReads, cacheReads, output);
        span.Finish(usage, written.ToString(), FinishReason(stopReason), responseId, responseModel);
        logger.LogInformation(
            "Wrote {OutputTokens} tokens from {InputTokens} with {Model} for {Cost} USD, stopping for {StopReason}",
            usage.OutputTokens, usage.InputTokens, request.Model, providers.Value.DeepSeek.PriceOf(request.Model, usage), stopReason);
        if (stopReason == "refusal")
        {
            throw new RpcException(new Status(StatusCode.FailedPrecondition, LoopErrors.WriterRefused));
        }
    }

    private static string? FinishReason(string? stopReason) => stopReason switch
    {
        null => null,
        "end_turn" or "stop_sequence" => "stop",
        "max_tokens" or "model_context_window_exceeded" => "length",
        "refusal" => "content_filter",
        _ => stopReason,
    };
}
