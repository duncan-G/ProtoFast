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
/// imports at once. It receives only as many messages as it has free slots for, so each is held
/// from its receive and kept off the queue for as long as its import runs. A message is deleted once the story is saved, the upload proves unreadable
/// or the discovery run fails on its own terms; any other failure comes back after its visibility
/// timeout, resumes the run it interrupted or reuses the one it outlived, and dead-letters after the queue's receive limit,
/// which is when its progress turns to failed.
/// </summary>
public sealed class DocumentImportConsumer(
    [FromKeyedServices(DocumentImportQueues.ImportQueueKey)] IMessageQueue queue,
    IServiceScopeFactory scopes,
    IRunLedger ledger,
    IOptions<DocumentImportConsumerOptions> options,
    ILogger<DocumentImportConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan ReceiveRetryDelay = TimeSpan.FromSeconds(5);

    private static readonly RunProgress Unreadable = new(
        RunPhase.Failed, Message: "This file couldn’t be read. Save it as a PDF, Word or text file and import it again.");

    private static readonly RunProgress Retrying = new(
        RunPhase.Retrying, Message: "Something went wrong. The import will pick up where it left off shortly.");

    private static readonly RunProgress GaveUp = new(
        RunPhase.Failed, Message: "This file couldn’t be turned into a story. Import it again to retry.");

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var maxImports = Math.Max(1, options.Value.MaxConcurrentImports);
        using var slots = new SemaphoreSlim(maxImports, maxImports);
        var running = new List<Task>();
        logger.LogInformation("Waiting for document imports, up to {MaxImports} at once", maxImports);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                running.RemoveAll(import => import.IsCompleted);
                await slots.WaitAsync(stoppingToken);
                var free = 1;
                while (free < maxImports && slots.Wait(0))
                {
                    free++;
                }

                IReadOnlyList<QueueMessage<DocumentImportRequested>> messages;
                try
                {
                    messages = await queue.ReceiveAsync<DocumentImportRequested>(free, stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    slots.Release(free);
                    logger.LogError(e, "Receiving document imports failed");
                    await Task.Delay(ReceiveRetryDelay, stoppingToken);
                    continue;
                }

                if (free > messages.Count)
                {
                    slots.Release(free - messages.Count);
                }

                foreach (var message in messages)
                {
                    running.Add(Task.Run(() => ImportInSlotAsync(message, slots, stoppingToken), CancellationToken.None));
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            await Task.WhenAll(running);
        }
    }

    private async Task ImportInSlotAsync(
        QueueMessage<DocumentImportRequested> message, SemaphoreSlim slots, CancellationToken ct)
    {
        try
        {
            await ImportAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogError(e, "Import of upload {UploadId} could not be settled", message.Body.UploadId);
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task ImportAsync(QueueMessage<DocumentImportRequested> message, CancellationToken ct)
    {
        var request = message.Body;
        using var activity = DocumentImportTelemetry.StartProcess(queue.Name, message.Parent);
        activity?.SetTag("document_import.upload_id", request.UploadId);
        await using var lease = MessageLease.Hold(queue, message, logger, ct);
        try
        {
            using var scope = scopes.CreateScope();
            var storyId = await scope.ServiceProvider.GetRequiredService<DocumentImportRunner>().RunAsync(request, lease.Token);
            await queue.DeleteAsync(message.ReceiptHandle, ct);
            activity?.SetTag("document_import.story_id", storyId);
            if (storyId is null)
            {
                logger.LogInformation("Upload {UploadId} is no longer on the desk; skipped", request.UploadId);
            }
            else
            {
                logger.LogInformation("Imported upload {UploadId} as story {StoryId}", request.UploadId, storyId);
            }
        }
        catch (UnreadableSourceException e)
        {
            activity.Fail(e);
            await queue.DeleteAsync(message.ReceiptHandle, ct);
            logger.LogError(e, "Import of upload {UploadId} abandoned", request.UploadId);
            await ReportAsync(request.UploadId, Unreadable, ct);
        }
        catch (DiscoveryFailedException e)
        {
            activity.Fail(e);
            await queue.DeleteAsync(message.ReceiptHandle, ct);
            logger.LogError(e, "Import of upload {UploadId} failed in discovery; it is not retried", request.UploadId);
            await ReportAsync(request.UploadId, GaveUp, ct);
        }
        catch (OperationCanceledException e) when (lease.IsLost)
        {
            // Another delivery owns the upload and its progress now, so nothing is reported here.
            activity.Fail(e);
            logger.LogError("Import of upload {UploadId} outlasted its lease and was delivered again; this attempt stops", request.UploadId);
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
