using System.Diagnostics;

namespace ProtoFast.DocumentImport.Core;

public static class DocumentImportTelemetry
{
    public const string SourceName = "ProtoFast.DocumentImport";

    public static readonly ActivitySource Source = new(SourceName);

    /// <summary>
    /// A messaging consumer span for one message, parented to the trace the sender was in, so an
    /// import shows up under the upload request that queued it.
    /// </summary>
    public static Activity? StartProcess(string queueName, ActivityContext parent) =>
        Source.StartActivity(ActivityKind.Consumer, parent, name: $"process {queueName}", tags:
        [
            new("messaging.system", "aws_sqs"),
            new("messaging.operation.type", "process"),
            new("messaging.destination.name", queueName),
        ]);

    public static void Fail(this Activity? activity, Exception e)
    {
        activity?.SetTag("error.type", e.GetType().FullName);
        activity?.SetStatus(ActivityStatusCode.Error, e.Message);
        activity?.AddException(e);
    }
}
