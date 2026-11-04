using System.Diagnostics;
using Grpc.Core;
using ProtoFast.Api.OffTheLoop;
using ProtoFast.Data.OffTheLoop.Entities;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Api.Services.OffTheLoop.ModelRuns;

/// <summary>
/// Runs a claimed run: its work is the Writer's, Researcher's or ResumeWriter's own request, made as the run's owner
/// in a scope of its own, as if they had made the call. What the call streams back is saved as
/// progress, and its findings as the result.
/// </summary>
public sealed class RunExecutor(
    IServiceScopeFactory scopes,
    RunStore store,
    RunOptions options,
    ILogger<RunExecutor> logger)
{
    public const string SourceName = "ProtoFast.Api.OffTheLoopRuns";

    private static readonly ActivitySource Source = new(SourceName);

    /// <param name="stopping">The host shutting down: the run goes back to the queue.</param>
    public async Task RunAsync(ClaimedRun claimed, CancellationToken stopping)
    {
        ActivityContext.TryParse(claimed.TraceParent, null, out var parent);
        using var activity = Source.StartActivity($"loop run {claimed.Label}", ActivityKind.Consumer, parent);
        activity?.SetTag("loop_run.id", claimed.Id);
        activity?.SetTag("loop_run.kind", claimed.Kind.ToString());
        activity?.SetTag("loop_run.attempt", claimed.Attempts);

        using var run = CancellationTokenSource.CreateLinkedTokenSource(stopping);
        var recorder = new RunRecorder(store, claimed.Id, run, options.ProgressEvery, claimed.Progress);
        if (recorder.Wrote)
        {
            // A model's reply can't be picked up part-way; what it wrote is kept for review.
            await store.FinishAsync(claimed.Id, ModelRunStatus.Failed, LoopErrors.WriterStopped, recorder.Progress, null, CancellationToken.None);
            return;
        }

        var heartbeat = HeartbeatAsync(claimed.Id, run);
        try
        {
            var result = await RunWorkAsync(claimed, recorder, run.Token);
            await store.FinishAsync(claimed.Id, ModelRunStatus.Succeeded, null, recorder.Progress, result, CancellationToken.None);
        }
        catch (Exception) when (stopping.IsCancellationRequested)
        {
            if (recorder.Wrote)
            {
                await store.FinishAsync(claimed.Id, ModelRunStatus.Failed, LoopErrors.WriterStopped, recorder.Progress, null, CancellationToken.None);
            }
            else
            {
                await store.ReleaseAsync(claimed.Id, CancellationToken.None);
            }
        }
        catch (Exception) when (run.IsCancellationRequested)
        {
            // Its owner cancelled or dismissed it; the row already says so.
        }
        catch (RpcException e)
        {
            activity?.SetStatus(ActivityStatusCode.Error, e.Status.Detail);
            await store.FinishAsync(claimed.Id, ModelRunStatus.Failed, e.Status.Detail, recorder.Progress, null, CancellationToken.None);
        }
        catch (Exception e)
        {
            logger.LogError(e, "Loop run {RunId} failed", claimed.Id);
            activity?.SetStatus(ActivityStatusCode.Error, e.Message);
            await store.FinishAsync(claimed.Id, ModelRunStatus.Failed, LoopErrors.RunUnexpected, recorder.Progress, null, CancellationToken.None);
        }
        finally
        {
            await run.CancelAsync();
            await heartbeat;
        }
    }

    private async Task<RunResult?> RunWorkAsync(ClaimedRun claimed, RunRecorder recorder, CancellationToken ct)
    {
        await using var scope = scopes.CreateAsyncScope();
        using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser(claimed.UserId);
        var context = new RunCallContext(ct);
        var work = claimed.Work;
        RunResult? result = null;
        switch (work.WorkCase)
        {
            case RunWork.WorkOneofCase.ResearchCompany:
                await scope.ServiceProvider.GetRequiredService<ResearcherService>().ResearchCompany(
                    work.ResearchCompany,
                    new RunStreamWriter<ResearchCompanyReply>(reply =>
                    {
                        if (reply.Step is { } step)
                        {
                            return recorder.StepAsync(step);
                        }

                        result = new RunResult { Company = reply.Findings };
                        return Task.CompletedTask;
                    }),
                    context);
                break;
            case RunWork.WorkOneofCase.ReadPosting:
                await scope.ServiceProvider.GetRequiredService<ResearcherService>().ReadPosting(
                    work.ReadPosting,
                    new RunStreamWriter<ReadPostingReply>(reply =>
                    {
                        if (reply.Step is { } step)
                        {
                            return recorder.StepAsync(step);
                        }

                        result = new RunResult { Posting = reply.Findings };
                        return Task.CompletedTask;
                    }),
                    context);
                break;
            case RunWork.WorkOneofCase.ReadResume:
                await scope.ServiceProvider.GetRequiredService<ResumeWriterService>().ReadResume(
                    work.ReadResume,
                    new RunStreamWriter<ReadResumeReply>(reply =>
                    {
                        if (reply.Step is { } step)
                        {
                            return recorder.StepAsync(step);
                        }

                        result = new RunResult { Resume = reply.Findings };
                        return Task.CompletedTask;
                    }),
                    context);
                break;
            case RunWork.WorkOneofCase.TailorResume:
                await scope.ServiceProvider.GetRequiredService<ResumeWriterService>().TailorResume(
                    work.TailorResume,
                    new RunStreamWriter<TailorResumeReply>(reply =>
                    {
                        if (reply.Step is { } step)
                        {
                            return recorder.StepAsync(step);
                        }

                        result = new RunResult { Resume = reply.Findings };
                        return Task.CompletedTask;
                    }),
                    context);
                break;
            case RunWork.WorkOneofCase.Write:
                await scope.ServiceProvider.GetRequiredService<WriterService>().Write(
                    work.Write,
                    new RunStreamWriter<WriteReply>(reply => recorder.TextAsync(reply.Text)),
                    context);
                break;
            default:
                throw LoopErrors.Invalid(LoopErrors.RunEmpty);
        }

        return result;
    }

    /// <summary>Stops the run when it is no longer running for this worker.</summary>
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
                logger.LogWarning(e, "Loop run {RunId} could not renew its lease", id);
            }
        }
    }
}
