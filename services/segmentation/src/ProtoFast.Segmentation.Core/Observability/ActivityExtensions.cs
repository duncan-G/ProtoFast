using System.Diagnostics;

namespace ProtoFast.Segmentation.Core.Observability;

/// <summary>
/// Marking a span failed, the same way everywhere (plan §25.1).
///
/// <para>A span that is never given a status is reported as unset, which every tracing backend
/// renders as "fine". That is how a run whose structurer threw still shows up green in the trace
/// view while the failure sits in the logs — the two halves of the same incident, filed
/// separately. These extensions are what keep them together.</para>
/// </summary>
public static class ActivityExtensions
{
    /// <summary>
    /// Records the exception on the span and marks it failed — unless the span is already failed,
    /// in which case nothing changes.
    ///
    /// <para>The first failure recorded is the one nearest the cause. A run's outer span learns
    /// that the run failed some time after an inner frame learned <em>why</em>, so letting the
    /// later, vaguer report win would bury the useful one. The exception goes on as a span event,
    /// stack trace included, which is what lets a trace answer "what threw" without a log
    /// correlation.</para>
    /// </summary>
    public static void Fail(this Activity? activity, Exception exception, string? description = null)
    {
        if (activity is null || activity.Status == ActivityStatusCode.Error)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, description ?? exception.Message);
        activity.SetTag("error.type", exception.GetType().FullName);
        activity.AddException(exception);
    }

    /// <summary>
    /// The same, for a failure that never took the form of an exception — a workflow event
    /// carrying a message, or an outcome enum. <paramref name="errorType"/> follows the
    /// OpenTelemetry convention: an exception type name, or a short domain code.
    /// </summary>
    public static void Fail(this Activity? activity, string errorType, string description)
    {
        if (activity is null || activity.Status == ActivityStatusCode.Error)
        {
            return;
        }

        activity.SetStatus(ActivityStatusCode.Error, description);
        activity.SetTag("error.type", errorType);
    }
}
