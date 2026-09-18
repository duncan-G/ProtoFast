namespace ProtoFast.Segmentation.Routing;

/// <summary>
/// The single switch for putting prompt and completion bodies on the <c>gen_ai.*</c> spans.
///
/// <para>Microsoft.Extensions.AI reads <c>OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT</c>
/// by itself — it is the OpenTelemetry semantic-convention variable, and it is what
/// <c>OpenTelemetryChatClient.EnableSensitiveData</c> defaults to. The workflow instrumentation in
/// Microsoft.Agents.AI.Workflows has no such default, so the same variable is read here and passed
/// to its options explicitly. One variable governs both layers rather than two that can disagree.
/// </para>
///
/// <para>Off unless the variable is exactly <c>true</c>, which is the comparison MEAI makes — "1"
/// and "yes" do not enable it there, so they do not enable it here either. It stays off by default
/// because these messages are document text and a document may be Confidential or Restricted
/// (plan §14.1); AppHost sets the variable in dev only.</para>
/// </summary>
public static class GenAiTelemetry
{
    public const string CaptureMessageContentVariable = "OTEL_INSTRUMENTATION_GENAI_CAPTURE_MESSAGE_CONTENT";

    /// <summary>Read once at startup, as MEAI also reads it once.</summary>
    public static bool CaptureMessageContent { get; } = string.Equals(
        Environment.GetEnvironmentVariable(CaptureMessageContentVariable),
        "true",
        StringComparison.OrdinalIgnoreCase);
}
