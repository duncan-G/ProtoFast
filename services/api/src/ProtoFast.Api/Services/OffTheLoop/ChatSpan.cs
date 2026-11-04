using System.Diagnostics;
using System.Text.Json;

namespace ProtoFast.Api.Services.OffTheLoop;

/// <summary>
/// An OpenTelemetry GenAI <c>chat</c> client span, which the Aspire dashboard renders as a conversation.
/// Prompt and reply text are recorded only when content capture is on.
/// </summary>
public sealed class ChatSpan : IDisposable
{
    public const string SourceName = "ProtoFast.OffTheLoop";

    private const string Operation = "chat";

    private static readonly ActivitySource Source = new(SourceName);

    private readonly Activity? _activity;
    private readonly string _model;
    private readonly bool _captureContent;

    private ChatSpan(Activity? activity, string model, bool captureContent)
    {
        _activity = activity;
        _model = model;
        _captureContent = captureContent;
    }

    public static ChatSpan Start(ChatRequest request, bool captureContent)
    {
        var activity = Source.StartActivity($"{Operation} {request.Model}", ActivityKind.Client);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag("gen_ai.operation.name", Operation);
            activity.SetTag("gen_ai.provider.name", request.Provider);
            activity.SetTag("gen_ai.request.model", request.Model);
            activity.SetTag("gen_ai.request.max_tokens", request.MaxOutputTokens);
            activity.SetTag("server.address", request.Endpoint.Host);
            activity.SetTag("server.port", request.Endpoint.Port);
            if (captureContent)
            {
                activity.SetTag("gen_ai.system_instructions", Json([new { type = "text", content = request.System }]));
                activity.SetTag("gen_ai.input.messages", Json([new { role = "user", parts = Text(request.User) }]));
            }
        }

        return new ChatSpan(activity, request.Model, captureContent);
    }

    /// <param name="finishReason">An OpenTelemetry GenAI finish reason: stop, length, content_filter or error.</param>
    public void Finish(ModelUsage usage, string text, string? finishReason, string? responseId, string? responseModel)
    {
        if (_activity is not { IsAllDataRequested: true })
        {
            return;
        }

        _activity.SetTag("gen_ai.response.id", responseId);
        _activity.SetTag("gen_ai.response.model", responseModel ?? _model);
        _activity.SetTag("gen_ai.usage.input_tokens", usage.InputTokens);
        _activity.SetTag("gen_ai.usage.output_tokens", usage.OutputTokens);
        if (finishReason is not null)
        {
            _activity.SetTag("gen_ai.response.finish_reasons", new[] { finishReason });
        }

        if (_captureContent)
        {
            _activity.SetTag("gen_ai.output.messages", Json(
                [new { role = "assistant", parts = Text(text), finish_reason = finishReason ?? "stop" }]));
        }
    }

    public void Fail(Exception e)
    {
        _activity?.SetTag("error.type", e.GetType().FullName);
        _activity?.SetStatus(ActivityStatusCode.Error, e.Message);
        _activity?.AddException(e);
    }

    public void Dispose() => _activity?.Dispose();

    private static object[] Text(string content) => [new { type = "text", content }];

    private static string Json(object[] value) => JsonSerializer.Serialize(value);
}
