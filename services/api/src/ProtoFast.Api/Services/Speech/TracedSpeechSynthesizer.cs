using System.Diagnostics;
using System.Text.Json;
using ProtoFast.DocumentImport.Core;

namespace ProtoFast.Api.Services.Speech;

/// <summary>
/// Wraps speaking a line in an OpenTelemetry GenAI <c>generate_content</c> client span, like
/// <see cref="Imaging.TracedImageGenerator"/>. The words and the audio are recorded only when
/// content capture is on.
/// </summary>
public sealed class TracedSpeechSynthesizer(
    ISpeechSynthesizer inner, string providerName, string model, string serverAddress, bool captureContent) : ISpeechSynthesizer
{
    private const string Operation = "generate_content";

    // Base64'd, well under the 4 MiB an OTLP gRPC export may carry.
    private const int AudioBudgetBytes = 1024 * 1024;

    public Task<SpeechVoice> VoiceForAsync(Guid? speakerId, CancellationToken ct) => inner.VoiceForAsync(speakerId, ct);

    public async Task<SpokenLine> SpeakAsync(string text, SpeechVoice voice, CancellationToken ct)
    {
        using var activity = SpeechTelemetry.Source.StartActivity($"{Operation} {model}", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("gen_ai.operation.name", Operation);
            activity.SetTag("gen_ai.provider.name", providerName);
            activity.SetTag("gen_ai.request.model", model);
            activity.SetTag("gen_ai.output.type", "speech");
            activity.SetTag("server.address", serverAddress);
            activity.SetTag("server.port", 443);
            activity.SetTag("protofast.speech.voice", voice.Id);
            activity.SetTag("protofast.speech.voice_name", voice.Name);
            activity.SetTag("protofast.speech.characters", text.Length);
            if (captureContent)
            {
                activity.SetTag("gen_ai.input.messages", Json([new { role = "user", parts = new object[] { new { type = "text", content = text } } }]));
            }
        }

        try
        {
            var line = await inner.SpeakAsync(text, voice, ct);
            if (activity is { IsAllDataRequested: true })
            {
                activity.SetTag("gen_ai.response.id", line.RequestId);
                activity.SetTag("gen_ai.response.model", model);
                if (captureContent)
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
            }

            return line;
        }
        catch (Exception e)
        {
            activity.Fail(e);
            throw;
        }
    }

    private static string Json(object[] value) => JsonSerializer.Serialize(value);
}
