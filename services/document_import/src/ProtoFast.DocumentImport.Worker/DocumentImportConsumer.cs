using Microsoft.Extensions.Options;
using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Worker.Import;
using ProtoFast.Storage;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Worker;

/// <summary>
/// Drains the import queue, running up to <see cref="DocumentImportConsumerOptions.MaxConcurrentImports"/>
/// imports at once. A message is deleted once the story is saved, the upload goes to the conversion worker
/// (which sends it back converted), the upload proves unreadable
/// or the discovery run fails on its own terms; any other failure comes back after its visibility
/// timeout, resumes the run it interrupted or reuses the one it outlived, and dead-letters after the queue's receive limit,
/// which is when its progress turns to failed. An import its owner cancels stops at the next poll, its message deleted and its run abandoned.
/// One the replica's stop cancels goes back on the queue at once (<see cref="QueueConsumer{T}"/>).
/// </summary>
public sealed class DocumentImportConsumer(
    [FromKeyedServices(DocumentImportQueues.ImportQueueKey)] IMessageQueue queue,
    IServiceScopeFactory scopes,
    IRunLedger ledger,
    IOptions<DocumentImportConsumerOptions> options,
    IHostApplicationLifetime lifetime,
    ILogger<DocumentImportConsumer> logger) : QueueConsumer<DocumentImportRequested>(queue, lifetime, logger)
{
    private static readonly RunProgress Unreadable = new(
        RunPhase.Failed, Message: "This file couldn’t be read. Save it as a PDF, Word or text file and import it again.");

    private static readonly RunProgress Retrying = new(
        RunPhase.Retrying, Message: "Something went wrong. The import will pick up where it left off shortly.");

    private const string CancelledReason = "Cancelled by its owner.";

    private static readonly RunProgress GaveUp = new(
        RunPhase.Failed, Message: "This file couldn’t be turned into a story. Import it again to retry.");

    protected override int Capacity => options.Value.MaxConcurrentImports;

    protected override async Task HandleAsync(QueueMessage<DocumentImportRequested> message, MessageLease lease)
    {
        var request = message.Body;
        var ct = lease.Token;
        using var activity = DocumentImportTelemetry.StartProcess(Queue.Name, message.Parent);
        activity?.SetTag("document_import.upload_id", request.UploadId);
        await using var cancellation = ImportCancellation.Watch(
            ledger, request.UploadId, options.Value.CancellationPollInterval, logger, lease.Token);
        try
        {
            using var scope = scopes.CreateScope();
            var outcome = await scope.ServiceProvider.GetRequiredService<DocumentImportRunner>().RunAsync(request, cancellation.Token);
            await Queue.DeleteAsync(message.ReceiptHandle, ct);
            activity?.SetTag("document_import.story_id", outcome.StoryId);
            if (outcome.SentForConversion)
            {
                logger.LogInformation("Upload {UploadId} went to the conversion worker; its import resumes when it comes back", request.UploadId);
            }
            else if (outcome.StoryId is not { } storyId)
            {
                logger.LogInformation("Upload {UploadId} is no longer on the desk; skipped", request.UploadId);
                await AbandonCancelledRunAsync(request.UploadId, ct);
            }
            else
            {
                logger.LogInformation("Imported upload {UploadId} as story {StoryId}", request.UploadId, storyId);
            }
        }
        catch (UnreadableSourceException e)
        {
            activity.Fail(e);
            await Queue.DeleteAsync(message.ReceiptHandle, ct);
            logger.LogError(e, "Import of upload {UploadId} abandoned", request.UploadId);
            await ReportAsync(request.UploadId, Unreadable, ct);
        }
        catch (DiscoveryFailedException e)
        {
            activity.Fail(e);
            await Queue.DeleteAsync(message.ReceiptHandle, ct);
            logger.LogError(e, "Import of upload {UploadId} failed in discovery; it is not retried", request.UploadId);
            await ReportAsync(request.UploadId, GaveUp, ct);
        }
        catch (OperationCanceledException e) when (lease.IsLost)
        {
            // Another delivery owns the upload and its progress now, so nothing is reported here.
            activity.Fail(e);
            logger.LogError("Import of upload {UploadId} outlasted its lease and was delivered again; this attempt stops", request.UploadId);
        }
        catch (OperationCanceledException) when (cancellation.IsCancelled)
        {
            await Queue.DeleteAsync(message.ReceiptHandle, ct);
            logger.LogInformation("Import of upload {UploadId} was cancelled by its owner", request.UploadId);
            await AbandonCancelledRunAsync(request.UploadId, ct);
        }
        catch (Exception e)
        {
            activity.Fail(e);
            if (e is OperationCanceledException)
            {
                throw;
            }

            if (message.IsLastDelivery)
            {
                logger.LogError(e, "Import of upload {UploadId} failed for the last time; it goes to the dead-letter queue", request.UploadId);
                await ReportAsync(request.UploadId, GaveUp, ct);
            }
            else
            {
                logger.LogError(e, "Import of upload {UploadId} failed; it will be redelivered", request.UploadId);
                await ReportAsync(request.UploadId, Retrying, ct);
            }
        }
    }

    // The attempt a cancel stopped leaves its run open; abandoned, it is never resumed or mined.
    private async Task AbandonCancelledRunAsync(string uploadId, CancellationToken ct)
    {
        try
        {
            var progress = (await ledger.ProgressAsync([uploadId], ct)).GetValueOrDefault(uploadId);
            if (progress is { Phase: RunPhase.Cancelled, RunId: { } runId } && await ledger.FindOpenAsync(runId, ct) is not null)
            {
                await ledger.AbandonAsync(runId, CancelledReason, ct);
            }
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "The run of cancelled upload {UploadId} could not be abandoned", uploadId);
        }
    }

    // A report that fails leaves the last one standing; the import's own outcome is already settled.
    private async Task ReportAsync(string uploadId, RunProgress progress, CancellationToken ct)
    {
        try
        {
            await ledger.ReportAsync(uploadId, progress, ct);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            logger.LogWarning(e, "Progress of upload {UploadId} could not be recorded", uploadId);
        }
    }
}
