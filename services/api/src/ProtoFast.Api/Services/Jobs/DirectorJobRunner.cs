using System.Diagnostics;
using Grpc.Core;
using ProtoFast.Api.Services.Directing;
using ProtoFast.Data.ThePlot.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Api.Services.Jobs;

/// <summary>
/// Runs a claimed job: each step is the Direction service's own request, replayed as the job's owner
/// in a scope of its own, as if the author had made the call. Progress is recorded after every step,
/// so a job taken over after a crash resumes where it stopped.
/// </summary>
public sealed class DirectorJobRunner(
    IServiceScopeFactory scopes,
    DirectorJobStore store,
    DirectorJobOptions options,
    ILogger<DirectorJobRunner> logger)
{
    public const string SourceName = "ProtoFast.Api.DirectorJobs";

    public const string Unexpected = "Something went wrong part-way through. Try again.";

    private static readonly ActivitySource Source = new(SourceName);

    /// <param name="stopping">The host shutting down: the job goes back to the queue.</param>
    public async Task RunAsync(ClaimedJob job, CancellationToken stopping)
    {
        ActivityContext.TryParse(job.TraceParent, null, out var parent);
        using var activity = Source.StartActivity($"director job {job.Label}", ActivityKind.Consumer, parent);
        activity?.SetTag("director_job.id", job.Id);
        activity?.SetTag("director_job.attempt", job.Attempts);

        using var run = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var heartbeat = HeartbeatAsync(job.Id, run);
        var steps = job.Steps;
        var results = job.Results;
        try
        {
            for (var at = job.StepsDone; at < steps.Steps.Count; at++)
            {
                var result = await RunStepAsync(job.UserId, steps.Steps[at], run.Token);
                KeepNewestDirection(results, result);
                results.Results.Add(result);
                if (job.FollowVariations)
                {
                    FollowVariation(steps, at, result);
                }

                await store.RecordStepAsync(job.Id, at + 1, steps, results, CancellationToken.None);
            }

            await store.FinishAsync(job.Id, DirectorJobStatus.Succeeded, null, CancellationToken.None);
        }
        catch (Exception) when (stopping.IsCancellationRequested)
        {
            await store.ReleaseAsync(job.Id, CancellationToken.None);
        }
        catch (Exception) when (run.IsCancellationRequested)
        {
            // Its owner cancelled it, or it was dismissed; the row already says so.
        }
        catch (RpcException e)
        {
            activity?.SetStatus(ActivityStatusCode.Error, e.Status.Detail);
            await store.FinishAsync(job.Id, DirectorJobStatus.Failed, e.Status.Detail, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Director job {JobId} failed", job.Id);
            activity?.SetStatus(ActivityStatusCode.Error, e.Message);
            await store.FinishAsync(job.Id, DirectorJobStatus.Failed, Unexpected, CancellationToken.None);
        }
        finally
        {
            await run.CancelAsync();
            await heartbeat;
        }
    }

    private async Task<JobStepResult> RunStepAsync(string userId, JobStep step, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(userId);
        var direction = scope.ServiceProvider.GetRequiredService<DirectionService>();
        var context = new JobCallContext(ct);
        return step.WorkCase switch
        {
            JobStep.WorkOneofCase.Direct => new JobStepResult { Direct = await direction.Direct(step.Direct, context) },
            JobStep.WorkOneofCase.Describe => new JobStepResult { Describe = await direction.Describe(step.Describe, context) },
            JobStep.WorkOneofCase.CastVoice => new JobStepResult { CastVoice = await direction.CastVoice(step.CastVoice, context) },
            JobStep.WorkOneofCase.Audio => new JobStepResult { Audio = await direction.GenerateAudio(step.Audio, context) },
            JobStep.WorkOneofCase.FrameImage => new JobStepResult { FrameImage = await direction.GenerateFrameImage(step.FrameImage, context) },
            JobStep.WorkOneofCase.LookImage => new JobStepResult { LookImage = await direction.GenerateLookImage(step.LookImage, context) },
            JobStep.WorkOneofCase.LibraryImage => new JobStepResult { LibraryImage = await direction.GenerateLibraryImage(step.LibraryImage, context) },
            _ => throw new RpcException(new Status(StatusCode.InvalidArgument, DirectorJobErrors.EmptyStep)),
        };
    }

    /// <summary>Stops the run when the job is no longer running for this worker.</summary>
    private async Task HeartbeatAsync(Guid id, CancellationTokenSource run)
    {
        while (!run.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(options.Heartbeat, run.Token);
                if (!await store.HeartbeatAsync(id, run.Token))
                {
                    await run.CancelAsync();
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception e)
            {
                // A missed beat only shortens the lease; the next one renews it.
                logger.LogWarning(e, "Director job {JobId} could not renew its lease", id);
            }
        }
    }

    /// <summary>A job keeps only its newest scene direction; each one supersedes the last.</summary>
    private static void KeepNewestDirection(JobResults results, JobStepResult next)
    {
        if (DirectionOf(next) is null)
        {
            return;
        }

        foreach (var result in results.Results)
        {
            switch (result.ReplyCase)
            {
                case JobStepResult.ReplyOneofCase.Direct:
                    result.Direct.Direction = null;
                    break;
                case JobStepResult.ReplyOneofCase.Audio:
                    result.Audio.Direction = null;
                    break;
                case JobStepResult.ReplyOneofCase.FrameImage:
                    result.FrameImage.Direction = null;
                    break;
                case JobStepResult.ReplyOneofCase.LookImage:
                    result.LookImage.Direction = null;
                    break;
            }
        }
    }

    private static SceneDirection? DirectionOf(JobStepResult result) => result.ReplyCase switch
    {
        JobStepResult.ReplyOneofCase.Direct => result.Direct.Direction,
        JobStepResult.ReplyOneofCase.Audio => result.Audio.Direction,
        JobStepResult.ReplyOneofCase.FrameImage => result.FrameImage.Direction,
        JobStepResult.ReplyOneofCase.LookImage => result.LookImage.Direction,
        _ => null,
    };

    /// <summary>
    /// Redrawing a frame makes the variation after it stale, since that is an edit of its picture:
    /// it is redrawn next unless a later step already does it.
    /// </summary>
    private static void FollowVariation(JobSteps steps, int at, JobStepResult result)
    {
        if (result.ReplyCase != JobStepResult.ReplyOneofCase.FrameImage || result.FrameImage.Direction is not { } direction)
        {
            return;
        }

        var request = steps.Steps[at].FrameImage;
        var frames = direction.Frames.OrderBy(f => f.Position).ToList();
        var index = frames.FindIndex(f => f.Id == request.FrameId);
        if (index < 0 || index + 1 >= frames.Count)
        {
            return;
        }

        var next = frames[index + 1];
        if (next.Variation.Length == 0 || next.Locked || next.Image is not { Stale: true }
            || steps.Steps.Skip(at + 1).Any(s => s.WorkCase == JobStep.WorkOneofCase.FrameImage && s.FrameImage.FrameId == next.Id))
        {
            return;
        }

        steps.Steps.Insert(at + 1, new JobStep { FrameImage = new GenerateFrameImageRequest { SceneId = request.SceneId, FrameId = next.Id } });
    }
}
