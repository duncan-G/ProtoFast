using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Services.Directing;
using ProtoFast.Api.Services.Jobs;
using ProtoFast.Data.ThePlot.Entities;
using Xunit;
using static ProtoFast.Api.IntegrationTests.Rows;

namespace ProtoFast.Api.IntegrationTests;

public class DirectorJobTests(StoryDatabase database)
{
    private readonly Writer _writer = new(database);

    /// <summary>A heading, an action and two of Mara's lines.</summary>
    private async Task<(Story Story, Scene Scene)> SceneAsync()
    {
        var story = await _writer.CreateStoryAsync();
        var mara = await _writer.CreateCharacterAsync(story.Id, "Mara");
        var docks = await _writer.CreateLocationAsync(story.Id, "Docks");
        var scene = await _writer.WriteFirstSceneAsync(
            story,
            Heading(docks.Id, "NIGHT"),
            Action("The radio crackles."),
            Dialogue(mara.Id, "Don't move."),
            Dialogue(mara.Id, "Listen."));
        return (story, scene);
    }

    /// <summary>An action line is read as written, so directing its cue needs no model.</summary>
    private static JobStep ActionCue(Scene scene) =>
        new() { Direct = new DirectRequest { SceneId = scene.Id, CueElementId = scene.Elements[1].Id } };

    private static JobStep DialogueCue(Scene scene, int row) =>
        new() { Direct = new DirectRequest { SceneId = scene.Id, CueElementId = scene.Elements[row].Id } };

    private Task<Job> StartAsync(Story story, string label, params JobStep[] steps) =>
        _writer.Jobs(async (j, c) => (await j.StartJob(new StartJobRequest { StoryId = story.Id, Label = label, Steps = { steps } }, c)).Job);

    private Task<GetJobReply> GetAsync(Job job) =>
        _writer.Jobs((j, c) => j.GetJob(new GetJobRequest { JobId = job.Id }, c));

    [Fact]
    public async Task A_queued_job_runs_as_its_owner_and_keeps_each_steps_reply()
    {
        var (story, scene) = await SceneAsync();

        var queued = await StartAsync(story, "Read the action", ActionCue(scene));

        Assert.Equal(JobStatus.Queued, queued.Status);
        Assert.Equal(scene.Id, queued.SceneId);
        Assert.Equal($"scene:{scene.Id}", queued.Lane);
        Assert.True(await _writer.RunNextJobAsync());
        var done = await GetAsync(queued);
        Assert.Equal(JobStatus.Succeeded, done.Job.Status);
        Assert.Equal(1, done.Job.StepsDone);
        Assert.Equal("The radio crackles.", Assert.Single(Assert.Single(done.Results).Direct.Direction.Cues).Performance);
        Assert.Equal("The radio crackles.", Assert.Single((await _writer.GetDirectionAsync(scene.Id)).Cues).Performance);
    }

    [Fact]
    public async Task Only_the_newest_scene_direction_of_a_job_is_kept()
    {
        var (story, scene) = await SceneAsync();
        _writer.ScriptModel("""{"cues":[{"line":"e3","performance":"[whispers] Don't move."}]}""");

        var job = await StartAsync(story, "Two cues", ActionCue(scene), DialogueCue(scene, 2));
        await _writer.RunNextJobAsync();

        var results = (await GetAsync(job)).Results;
        Assert.Equal(2, results.Count);
        Assert.Null(results[0].Direct.Direction);
        Assert.Equal(2, results[1].Direct.Direction.Cues.Count);
        var later = await _writer.Jobs((j, c) => j.GetJob(new GetJobRequest { JobId = job.Id, FromStep = 1 }, c));
        Assert.Single(later.Results);
    }

    [Fact]
    public async Task A_failed_step_stops_the_job_with_the_words_the_call_would_have_given()
    {
        var (story, scene) = await SceneAsync();
        _writer.ScriptModel("""{"cues":[{"line":"e3","performance":"Do [sad] move."}]}""");

        var job = await StartAsync(story, "Cues", DialogueCue(scene, 2), ActionCue(scene));
        await _writer.RunNextJobAsync();

        var failed = (await GetAsync(job)).Job;
        Assert.Equal(JobStatus.Failed, failed.Status);
        Assert.Equal(DirectionErrors.ModelUnusable, failed.Error);
        Assert.Equal(0, failed.StepsDone);
        Assert.Empty((await _writer.GetDirectionAsync(scene.Id)).Cues);
    }

    [Fact]
    public async Task Jobs_on_one_scene_take_turns_while_the_library_runs_beside_them()
    {
        var (story, scene) = await SceneAsync();
        var first = await StartAsync(story, "First", ActionCue(scene));
        var second = await StartAsync(story, "Second", ActionCue(scene));
        var store = database.Services.GetRequiredService<DirectorJobStore>();
        var ct = TestContext.Current.CancellationToken;

        var claimed = await store.ClaimAsync(ct, _writer.UserId);

        Assert.Equal(first.Id, claimed?.Id.ToString());
        Assert.Null(await store.ClaimAsync(ct, _writer.UserId));
        var library = await StartAsync(story, "Sheet", new JobStep { CastVoice = new CastVoiceRequest { CharacterId = Guid.NewGuid().ToString() } });
        Assert.Equal($"library:{story.Id}", library.Lane);
        Assert.Equal(library.Id, (await store.ClaimAsync(ct, _writer.UserId))?.Id.ToString());

        await store.FinishAsync(claimed!.Id, DirectorJobStatus.Succeeded, null, ct);
        Assert.Equal(second.Id, (await store.ClaimAsync(ct, _writer.UserId))?.Id.ToString());
    }

    [Fact]
    public async Task A_cancelled_job_never_starts_and_a_running_one_stops_writing()
    {
        var (story, scene) = await SceneAsync();
        var queued = await StartAsync(story, "Queued", ActionCue(scene));
        var running = await StartAsync(story, "Running", ActionCue(scene));
        var store = database.Services.GetRequiredService<DirectorJobStore>();
        var ct = TestContext.Current.CancellationToken;

        var cancelled = (await _writer.Jobs((j, c) => j.CancelJob(new CancelJobRequest { JobId = queued.Id }, c))).Job;

        Assert.Equal(JobStatus.Cancelled, cancelled.Status);
        var claimed = await store.ClaimAsync(ct, _writer.UserId);
        Assert.Equal(running.Id, claimed?.Id.ToString());
        await _writer.Jobs((j, c) => j.CancelJob(new CancelJobRequest { JobId = running.Id }, c));
        Assert.False(await store.HeartbeatAsync(claimed!.Id, ct));
        await store.FinishAsync(claimed.Id, DirectorJobStatus.Succeeded, null, ct);
        Assert.Equal(JobStatus.Cancelled, (await GetAsync(running)).Job.Status);
    }

    [Fact]
    public async Task A_job_released_at_shutdown_resumes_after_the_steps_it_finished()
    {
        var (story, scene) = await SceneAsync();
        var job = await StartAsync(story, "Two", ActionCue(scene), ActionCue(scene));
        var store = database.Services.GetRequiredService<DirectorJobStore>();
        var ct = TestContext.Current.CancellationToken;
        var claimed = await store.ClaimAsync(ct, _writer.UserId);
        await store.RecordStepAsync(claimed!.Id, 1, claimed.Steps, claimed.Results, ct);

        await store.ReleaseAsync(claimed.Id, ct);

        var again = await store.ClaimAsync(ct, _writer.UserId);
        Assert.Equal(1, again?.StepsDone);
        Assert.Equal(1, again?.Attempts);
        Assert.Equal(JobStatus.Running, (await GetAsync(job)).Job.Status);
    }

    [Fact]
    public async Task Finished_jobs_can_be_dismissed_but_running_ones_stay()
    {
        var (story, scene) = await SceneAsync();
        var done = await StartAsync(story, "Done", ActionCue(scene));
        await _writer.RunNextJobAsync();
        var queued = await StartAsync(story, "Queued", ActionCue(scene));

        await _writer.Jobs((j, c) => j.DismissJobs(new DismissJobsRequest { JobIds = { done.Id, queued.Id } }, c));

        var left = (await _writer.Jobs((j, c) => j.ListJobs(new ListJobsRequest(), c))).Jobs;
        Assert.Equal(queued.Id, Assert.Single(left).Id);
    }

    [Fact]
    public async Task Nobody_else_sees_or_queues_onto_a_writers_story()
    {
        var (story, scene) = await SceneAsync();
        var job = await StartAsync(story, "Mine", ActionCue(scene));
        var other = new Writer(database);

        Assert.Empty((await other.Jobs((j, c) => j.ListJobs(new ListJobsRequest(), c))).Jobs);
        var hidden = await Assert.ThrowsAsync<RpcException>(() => other.Jobs((j, c) => j.GetJob(new GetJobRequest { JobId = job.Id }, c)));
        Assert.Equal(StatusCode.NotFound, hidden.StatusCode);
        var refused = await Assert.ThrowsAsync<RpcException>(() => other.Jobs((j, c) =>
            j.StartJob(new StartJobRequest { StoryId = story.Id, Label = "Theirs", Steps = { ActionCue(scene) } }, c)));
        Assert.Equal(StatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task A_job_keeps_to_one_scene()
    {
        var (story, scene) = await SceneAsync();
        var elsewhere = new JobStep { Audio = new GenerateAudioRequest { SceneId = Guid.NewGuid().ToString() } };

        var error = await Assert.ThrowsAsync<RpcException>(() => StartAsync(story, "Both", ActionCue(scene), elsewhere));

        Assert.Equal(DirectorJobErrors.ScenesDiffer, error.Status.Detail);
    }
}
