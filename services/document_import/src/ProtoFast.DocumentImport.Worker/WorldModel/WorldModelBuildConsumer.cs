using Microsoft.Extensions.Options;
using ProtoFast.DocumentImport.Core;
using ProtoFast.Storage;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.DocumentImport.Worker.WorldModel;

/// <summary>
/// Drains the world-model queue, running up to <see cref="WorldModelBuildOptions.MaxConcurrentBuilds"/>
/// builds at once, each held off the queue for as long as it runs. A message is deleted once its
/// build finishes or turns out to be gone; any other failure marks the build retrying and comes
/// back after its visibility timeout, resuming at the first scene not yet read, until the queue's
/// receive limit sends it to the dead-letter queue and the build is marked failed. A build its
/// owner cancels stops at the next poll.
/// </summary>
public sealed class WorldModelBuildConsumer(
    [FromKeyedServices(WorldModelQueues.BuildQueueKey)] IMessageQueue queue,
    IServiceScopeFactory scopes,
    WorldModelLedger ledger,
    IOptions<WorldModelBuildOptions> options,
    ILogger<WorldModelBuildConsumer> logger) : BackgroundService
{
    private static readonly TimeSpan ReceiveRetryDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var maxBuilds = Math.Max(1, options.Value.MaxConcurrentBuilds);
        using var slots = new SemaphoreSlim(maxBuilds, maxBuilds);
        var running = new List<Task>();
        logger.LogInformation("Waiting for world model builds, up to {MaxBuilds} at once", maxBuilds);
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                running.RemoveAll(build => build.IsCompleted);
                await slots.WaitAsync(stoppingToken);
                var free = 1;
                while (free < maxBuilds && slots.Wait(0))
                {
                    free++;
                }

                IReadOnlyList<QueueMessage<WorldModelBuildRequested>> messages;
                try
                {
                    messages = await queue.ReceiveAsync<WorldModelBuildRequested>(free, stoppingToken);
                }
                catch (Exception e) when (!stoppingToken.IsCancellationRequested)
                {
                    slots.Release(free);
                    logger.LogError(e, "Receiving world model builds failed");
                    await Task.Delay(ReceiveRetryDelay, stoppingToken);
                    continue;
                }

                if (free > messages.Count)
                {
                    slots.Release(free - messages.Count);
                }

                foreach (var message in messages)
                {
                    running.Add(Task.Run(() => BuildInSlotAsync(message, slots, stoppingToken), CancellationToken.None));
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

    private async Task BuildInSlotAsync(QueueMessage<WorldModelBuildRequested> message, SemaphoreSlim slots, CancellationToken ct)
    {
        try
        {
            await BuildAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
        }
        catch (Exception e)
        {
            logger.LogError(e, "Build {BuildId} could not be settled", message.Body.BuildId);
        }
        finally
        {
            slots.Release();
        }
    }

    private async Task BuildAsync(QueueMessage<WorldModelBuildRequested> message, CancellationToken ct)
    {
        var request = message.Body;
        using var activity = DocumentImportTelemetry.StartProcess(queue.Name, message.Parent);
        activity?.SetTag("world_model.build_id", request.BuildId);
        activity?.SetTag("world_model.story_id", request.StoryId);
        await using var lease = MessageLease.Hold(queue, message, logger, ct);
        await using var cancellation = BuildCancellation.Watch(
            ledger, request.BuildId, request.UserId, options.Value.CancellationPollInterval, logger, lease.Token);
        try
        {
            using var scope = scopes.CreateScope();
            var built = await scope.ServiceProvider.GetRequiredService<WorldModelBuildRunner>().RunAsync(request, cancellation.Token);
            await queue.DeleteAsync(message.ReceiptHandle, ct);
            logger.LogInformation(
                built ? "Built the world model of story {StoryId} for build {BuildId}" : "Build {BuildId} of story {StoryId} was no longer there to run; skipped",
                request.StoryId, request.BuildId);
        }
        catch (OperationCanceledException e) when (lease.IsLost)
        {
            // Another delivery owns the build now, so nothing is reported here.
            activity.Fail(e);
            logger.LogError("Build {BuildId} outlasted its lease and was delivered again; this attempt stops", request.BuildId);
        }
        catch (OperationCanceledException) when (cancellation.IsCancelled)
        {
            await queue.DeleteAsync(message.ReceiptHandle, ct);
            logger.LogInformation("Build {BuildId} was cancelled by its owner", request.BuildId);
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
                logger.LogError(e, "Build {BuildId} failed for the last time; it goes to the dead-letter queue", request.BuildId);
                await ReportFailureAsync(request, e, final: true, ct);
            }
            else
            {
                logger.LogError(e, "Build {BuildId} failed; it will be redelivered", request.BuildId);
                await ReportFailureAsync(request, e, final: false, ct);
            }
        }
    }

    // A report that fails leaves the last one standing; the build's own outcome is already settled.
    private async Task ReportFailureAsync(WorldModelBuildRequested request, Exception e, bool final, CancellationToken ct)
    {
        try
        {
            await ledger.FailAsync(request.BuildId, request.UserId, e.Message, final, ct);
        }
        catch (Exception failure) when (failure is not OperationCanceledException)
        {
            logger.LogWarning(failure, "Failure of build {BuildId} could not be recorded", request.BuildId);
        }
    }
}
