using System.Text;
using System.Text.Json;
using Microsoft.Extensions.AI;
using ProtoFast.Segmentation.Core.Model;

namespace ProtoFast.Segmentation.Routing.Providers;

/// <summary>
/// Serves recorded provider responses instead of calling anybody (plan §22.2).
///
/// <para>This is what the contract tests run against — including the recorded 429s and malformed
/// JSON that the repair loop and the retry policy have to handle — and what makes the pipeline
/// developable without four API keys. Recordings are keyed by a hash of the prompt, so a changed
/// prompt misses its recording loudly rather than replaying an answer to a different question.</para>
/// </summary>
public sealed class ReplayChatClient(string recordingsPath, string model) : IChatClient
{
    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        var key = KeyFor(messages);
        var path = Path.Combine(recordingsPath, $"{key}.json");

        if (!File.Exists(path))
        {
            throw new ProviderException(
                ProviderErrorKind.BadRequest,
                "replay",
                $"No recording for prompt hash {key} under '{recordingsPath}'. " +
                "Record it with `ProtoFast.Segmentation.Cli record`, or the prompt changed and the " +
                "recording is stale.");
        }

        var recording = JsonSerializer.Deserialize<Recording>(
            await File.ReadAllTextAsync(path, cancellationToken),
            JsonOptions)!;

        if (recording.ErrorKind is { } errorKind)
        {
            throw new ProviderException(errorKind, "replay", recording.ErrorMessage ?? "recorded failure")
            {
                RetryAfter = recording.RetryAfterSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null,
            };
        }

        return new ChatResponse(new ChatMessage(ChatRole.Assistant, recording.Text))
        {
            ModelId = model,
            Usage = new UsageDetails
            {
                InputTokenCount = recording.InputTokens,
                OutputTokenCount = recording.OutputTokens,
            },
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var response = await GetResponseAsync(messages, options, cancellationToken);
        foreach (var update in response.ToChatResponseUpdates())
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        serviceType.IsInstanceOfType(this) ? this : null;

    public void Dispose()
    {
    }

    /// <summary>The recording's filename: a hash of every message's role and text, in order.</summary>
    public static string KeyFor(IEnumerable<ChatMessage> messages)
    {
        var builder = new StringBuilder();
        foreach (var message in messages)
        {
            builder.Append(message.Role.Value).Append('\n').Append(message.Text).Append('\n');
        }

        return Ids.Sha256Hex(builder.ToString())[..16];
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    /// <summary>One recorded exchange. <see cref="ErrorKind"/> records a failure instead of a reply.</summary>
    public sealed record Recording(string Text)
    {
        public int InputTokens { get; init; }

        public int OutputTokens { get; init; }

        public ProviderErrorKind? ErrorKind { get; init; }

        public string? ErrorMessage { get; init; }

        public double? RetryAfterSeconds { get; init; }
    }
}
