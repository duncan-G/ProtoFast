using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ProtoFast.Storage.Abstractions;

namespace ProtoFast.Storage;

/// <summary>
/// QueueConsumer takes messages off a queue, up to <see cref="Capacity"/> at once, and holds each
/// with a <see cref="MessageLease"/> while <see cref="HandleAsync"/> works on it. It takes no more
/// once the host starts stopping, so a draining replica only finishes what it has. A message the
/// stop cancels before it is settled goes back on the queue at once, for another replica.
/// </summary>
public abstract class QueueConsumer<T>(IMessageQueue queue, IHostApplicationLifetime lifetime, ILogger logger) : BackgroundService
{
    private static readonly TimeSpan ReceiveRetryDelay = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan HandBackTimeout = TimeSpan.FromSeconds(5);

    protected IMessageQueue Queue => queue;

    /// <summary>How many messages are worked on at once.</summary>
    protected virtual int Capacity => 1;

    /// <summary>
    /// Works on <paramref name="message"/> under <paramref name="lease"/>'s token, which is
    /// cancelled at stop or when the lease is lost. Returning leaves the message as the handler
    /// left it: deleted, or invisible until its timeout lapses for a redelivery.
    /// </summary>
    protected abstract Task HandleAsync(QueueMessage<T> message, MessageLease lease);

    protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // ApplicationStopping fires at SIGTERM, before the drain; stoppingToken only after it.
        using var intake = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, lifetime.ApplicationStopping);
        var capacity = Math.Max(1, Capacity);
        using var slots = new SemaphoreSlim(capacity, capacity);
        var running = new List<Task>();
        logger.LogInformation("Taking messages from {Queue}, up to {Capacity} at once", queue.Name, capacity);
        try
        {
            while (!intake.IsCancellationRequested)
            {
                running.RemoveAll(task => task.IsCompleted);
                await slots.WaitAsync(intake.Token);
                var free = 1;
                while (free < capacity && slots.Wait(0))
                {
                    free++;
                }

                IReadOnlyList<QueueMessage<T>> messages;
                try
                {
                    messages = await queue.ReceiveAsync<T>(free, intake.Token);
                }
                catch (Exception e) when (!intake.IsCancellationRequested)
                {
                    slots.Release(free);
                    logger.LogError(e, "Receiving from {Queue} failed", queue.Name);
                    await Task.Delay(ReceiveRetryDelay, intake.Token);
                    continue;
                }

                if (free > messages.Count)
                {
                    slots.Release(free - messages.Count);
                }

                foreach (var message in messages)
                {
                    running.Add(intake.IsCancellationRequested
                        ? HandBackAsync(message)
                        : Task.Run(() => HandleInSlotAsync(message, slots, stoppingToken), CancellationToken.None));
                }
            }
        }
        catch (OperationCanceledException) when (intake.IsCancellationRequested)
        {
        }
        finally
        {
            logger.LogInformation("Stopped taking messages from {Queue}; finishing {Count}", queue.Name, running.Count(task => !task.IsCompleted));
            await Task.WhenAll(running);
        }
    }

    private async Task HandleInSlotAsync(QueueMessage<T> message, SemaphoreSlim slots, CancellationToken stoppingToken)
    {
        var handBack = false;
        try
        {
            // Disposed before any hand-back, so a renewal can't hide the message again.
            await using var lease = MessageLease.Hold(queue, message, logger, stoppingToken);
            try
            {
                await HandleAsync(message, lease);
            }
            catch (OperationCanceledException) when (lease.IsLost)
            {
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                handBack = true;
            }
        }
        catch (Exception e)
        {
            logger.LogError(e, "A message from {Queue} could not be handled", queue.Name);
        }
        finally
        {
            slots.Release();
        }

        if (handBack)
        {
            await HandBackAsync(message);
        }
    }

    // A visibility of zero puts the message back on the queue now, not when its lease lapses.
    private async Task HandBackAsync(QueueMessage<T> message)
    {
        using var timeout = new CancellationTokenSource(HandBackTimeout);
        try
        {
            await queue.ExtendVisibilityAsync(message.ReceiptHandle, TimeSpan.Zero, timeout.Token);
        }
        catch (Exception e)
        {
            logger.LogWarning(e, "A message from {Queue} could not be handed back; it returns when its lease lapses", queue.Name);
        }
    }
}
