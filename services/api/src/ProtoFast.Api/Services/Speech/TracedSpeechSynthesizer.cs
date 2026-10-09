using System.Diagnostics;
using System.Text.Json;

namespace ProtoFast.Api.Services.Speech;

/// <summary>
/// Wraps speaking a line in an OpenTelemetry GenAI <c>generate_content</c> client span, like
/// <c>TracedImageGenerator</c>. The words and the audio are recorded only when content capture is on.
/// </summary>
public sealed class TracedSpeechSynthesizer(ISpeechSynthesizer inner, string serverAddress, bool captureContent) : ISpeechSynthesizer
{
    private const string Operation = "generate_content";

    // Base64'd, well under the 4 MiB an OTLP gRPC export may carry.
    private const int AudioBudgetBytes = 1024 * 1024;

    public string Provider => inner.Provider;

    public string Model => inner.Model;

    public SpeechRequest Prepare(string performance, string? voiceId) => inner.Prepare(performance, voiceId);

    public async Task<SpokenLine> SpeakAsync(SpeechRequest request, CancellationToken ct)
    {
        using var activity = SpeechTelemetry.Source.StartActivity($"{Operation} {inner.Model}", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("gen_ai.operation.name", Operation);
            activity.SetTag("gen_ai.provider.name", inner.Provider);
            activity.SetTag("gen_ai.request.model", inner.Model);
            activity.SetTag("gen_ai.output.type", "speech");
            activity.SetTag("server.address", serverAddress);
            activity.SetTag("server.port", 443);
            activity.SetTag("protofast.speech.voice", request.Voice);
            activity.SetTag("protofast.speech.characters", request.Text.Length);
            if (captureContent)
            {
                if (!string.IsNullOrEmpty(request.Instructions))
                {
                    activity.SetTag("gen_ai.system_instructions", Json([new { type = "text", content = request.Instructions }]));
                }

                activity.SetTag("gen_ai.input.messages", Json([new { role = "user", parts = new object[] { new { type = "text", content = request.Text } } }]));
            }
        }

        try
        {
            var line = await inner.SpeakAsync(request, ct);
            if (activity is { IsAllDataRequested: true } && captureContent)
            {
                activity.SetTag("gen_ai.output.messages", Json(
                [
                    new
                    {
                        role = "assistant",
                        parts = new[] { new GenAiMedia(AudioBudgetBytes).Part("audio", line.Audio, line.ContentType) },
                        finish_reason = "stop",
                    },
                ]));
            }

            return line;
        }
        catch (Exception e)
        {
            activity?.SetTag("error.type", e.GetType().FullName);
            activity?.SetStatus(ActivityStatusCode.Error, e.Message);
            activity?.AddException(e);
            throw;
        }
    }

    public Task<IReadOnlyList<SpeechVoice>> ListVoicesAsync(CancellationToken ct) => inner.ListVoicesAsync(ct);

    public Task<IReadOnlyList<SpeechVoice>> SearchVoicesAsync(VoiceQuery query, CancellationToken ct) => inner.SearchVoicesAsync(query, ct);

    private static string Json(object[] value) => JsonSerializer.Serialize(value);
}
