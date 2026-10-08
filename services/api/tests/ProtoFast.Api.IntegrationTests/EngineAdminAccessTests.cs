using System.Text.Json.Nodes;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Api.Admin.Theplot;
using ProtoFast.Api.Services.Admin;
using ProtoFast.Data.ThePlot.Repositories;
using ProtoFast.Database.Abstractions;
using ProtoFast.DocumentImport.Core;
using Xunit;
using Engine = ProtoFast.DocumentImport.Engine;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>Who may read the engine through theplot's console, and what they see.</summary>
public sealed class EngineAdminAccessTests(StoryDatabase database)
{
    private readonly Operator _theplot = new(database, "operators", "admin-theplot");

    private Engine.Storage.IRunLedger Ledger => database.Services.GetRequiredService<Engine.Storage.IRunLedger>();

    [Fact]
    public async Task Another_apps_operator_cannot_list_runs_or_families()
    {
        var outsider = new Operator(database, "operators", "admin-protofast", "platform");

        var runs = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotRunsService, ListRunsReply>(
            (s, c) => s.ListRuns(new ListRunsRequest(), c)));
        var families = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotFamiliesService, ListFamiliesReply>(
            (s, c) => s.ListFamilies(new ListFamiliesRequest(), c)));

        Assert.Equal(StatusCode.PermissionDenied, runs.StatusCode);
        Assert.Equal(StatusCode.PermissionDenied, families.StatusCode);
    }

    [Fact]
    public async Task Another_apps_operator_fetching_a_run_by_id_gets_not_found()
    {
        var runId = await OpenRunAsync("screenplay");
        var outsider = new Operator(database, "operators", "admin-protofast");

        var error = await Assert.ThrowsAsync<RpcException>(() => outsider.Call<TheplotRunsService, GetRunReply>(
            (s, c) => s.GetRun(new GetRunRequest { RunId = runId }, c)));

        Assert.Equal(StatusCode.NotFound, error.StatusCode);
    }

    [Fact]
    public async Task Theplots_operator_reads_a_run_with_its_attempts_and_transcript()
    {
        var runId = await OpenRunAsync("screenplay#2");
        await Ledger.RecordAsync(Attempt(runId), default);
        await Ledger.RecordAsync(runId, new Engine.Executors.Decision("executor:story", "small@1", "cheap", 0.7), default);
        await Ledger.AppendTranscriptAsync(runId, 0, """{"message":{"role":"User","text":"Import this.","toolCalls":[],"toolResults":[]}}""", default);
        await Ledger.AppendTranscriptAsync(
            runId, 1,
            """{"message":{"role":"Assistant","text":null,"toolCalls":[{"id":"c1","name":"execute_skill","input":{"skill":"context"}}],"toolResults":[]},"spent":{"amount":0.0125,"duration":"00:00:03.5000000"}}""",
            default);
        await Ledger.RecordSystemPromptAsync(runId, 0, "You are the discovery agent of a document import engine.", default);

        var listed = await _theplot.Call<TheplotRunsService, ListRunsReply>(
            (s, c) => s.ListRuns(new ListRunsRequest { Family = "screenplay", PageSize = 100 }, c));
        var run = await _theplot.Call<TheplotRunsService, GetRunReply>((s, c) => s.GetRun(new GetRunRequest { RunId = runId }, c));
        var transcript = await _theplot.Call<TheplotRunsService, GetTranscriptReply>(
            (s, c) => s.GetTranscript(new GetTranscriptRequest { RunId = runId }, c));

        var header = Assert.Single(listed.Runs, r => r.RunId == runId);
        Assert.Equal(("screenplay", 2, RunStatus.Open, 1, true), (header.Family, header.Generation, header.Status, header.StageAttempts, header.Passed));
        var stage = Assert.Single(run.Stages);
        Assert.Equal(("story", "small", ExecutorTier.DelegateSmall, 250_000L), (stage.StageId, stage.ExecutorId, stage.Tier, stage.Spend.UsdMicros));
        Assert.Equal(Verdict.Pass, Assert.Single(stage.Verdicts).Verdict);
        Assert.Equal("small@1", Assert.Single(run.Decisions).Choice);
        Assert.Equal(2, run.MessageCount);
        Assert.Equal(2, transcript.Total);
        var prompt = Assert.Single(transcript.SystemPrompts);
        Assert.Equal((0, "You are the discovery agent of a document import engine."), (prompt.FromSequence, prompt.Text));
        Assert.Equal(TranscriptRole.User, transcript.Messages[0].Role);
        var turn = transcript.Messages[1];
        Assert.Equal(("execute_skill", 12_500L, 3_500L), (turn.ToolCalls[0].Name, turn.Spend.UsdMicros, turn.Spend.DurationMs));
        // jsonb re-spaces what it stores, so the input is compared as JSON.
        Assert.Equal("context", JsonNode.Parse(turn.ToolCalls[0].InputJson)!["skill"]!.GetValue<string>());
    }

    [Fact]
    public async Task Theplots_operator_sees_a_run_named_after_another_writers_upload()
    {
        var runId = await OpenRunAsync("screenplay");
        var uploadId = DocumentImportIds.New();
        await using (var scope = database.Services.CreateAsyncScope())
        {
            using var user = scope.ServiceProvider.GetRequiredService<UserContext>().SetCurrentUser($"writer-{Guid.NewGuid():N}");
            using var unitOfWork = scope.ServiceProvider.GetRequiredService<IUnitOfWorkFactory>().CreateReadWrite("seed");
            await scope.ServiceProvider.GetRequiredService<IDocumentUploadRepository>().AddAsync(new Data.ThePlot.Entities.DocumentUpload
            {
                UploadId = uploadId, FileName = "the-quiet-year.fdx", SizeBytes = 10, MediaType = "application/xml", FileExtension = ".fdx",
            }, default);
            await unitOfWork.CommitAsync(default);
        }

        await Ledger.ReportAsync(uploadId, new Engine.Storage.RunProgress(Engine.Storage.RunPhase.Running, runId), default);
        var unnamed = await OpenRunAsync("screenplay");

        var listed = await _theplot.Call<TheplotRunsService, ListRunsReply>(
            (s, c) => s.ListRuns(new ListRunsRequest { Family = "screenplay", PageSize = 100 }, c));
        var run = await _theplot.Call<TheplotRunsService, GetRunReply>((s, c) => s.GetRun(new GetRunRequest { RunId = runId }, c));

        Assert.Equal("the-quiet-year.fdx", Assert.Single(listed.Runs, r => r.RunId == runId).Name);
        Assert.Equal("", Assert.Single(listed.Runs, r => r.RunId == unnamed).Name);
        Assert.Equal("the-quiet-year.fdx", run.Run.Name);
    }

    [Fact]
    public async Task Theplots_operator_reads_a_runs_steps_with_its_brief_and_asks_for_another()
    {
        var runId = await OpenRunAsync("screenplay");
        await Ledger.AppendTranscriptAsync(runId, 0, """{"message":{"role":"User","text":"Import this."}}""", default);
        await Ledger.RecordStepAsync(runId, new Engine.Briefing.RunStep(1,
        [
            new Engine.Briefing.StepCall("c1", "execute_code", "importer", "import", false,
            [
                new Engine.Briefing.StepEffect(Engine.Briefing.StepEffectKind.ScriptRan, "Ran importer@2/import", 0, "importer@2"),
                new Engine.Briefing.StepEffect(Engine.Briefing.StepEffectKind.ArtifactWritten, "Wrote stage `story`: passed", 1, "story/h"),
            ]),
        ]), default);
        await Ledger.CloseAsync(runId, null, default);
        var briefs = database.Services.GetRequiredService<Engine.Briefing.IRunBriefs>();
        Assert.True(await briefs.ClaimAsync(runId, 3, TimeSpan.FromMinutes(15), default));
        await briefs.CompleteAsync(runId, new Engine.Briefing.RunBrief(
            "Delivered.", "Ran the importer.", [new Engine.Briefing.BriefFlag("manuscript-specific-skill", "Hardcoded cast.", 1)],
            [new Engine.Briefing.StepBrief(1, "Ran the family's importer, which hardcodes the cast.")], "scripted", 0.0042m), default);

        var review = await _theplot.Call<TheplotRunsService, GetRunReviewReply>(
            (s, c) => s.GetRunReview(new GetRunReviewRequest { RunId = runId }, c));
        await _theplot.Call<TheplotRunsService, RebriefRunReply>((s, c) => s.RebriefRun(new RebriefRunRequest { RunId = runId }, c));
        var rebriefing = await _theplot.Call<TheplotRunsService, GetRunReviewReply>(
            (s, c) => s.GetRunReview(new GetRunReviewRequest { RunId = runId }, c));
        var missing = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotRunsService, RebriefRunReply>(
            (s, c) => s.RebriefRun(new RebriefRunRequest { RunId = "no-such-run" }, c)));

        Assert.Equal((BriefStatus.Briefed, "Delivered.", 4_200L), (review.Brief.Status, review.Brief.Outcome, review.Brief.CostUsdMicros));
        Assert.Equal(("manuscript-specific-skill", 1), (review.Brief.Flags[0].Kind, review.Brief.Flags[0].Sequence));
        var step = Assert.Single(review.Steps);
        Assert.Equal(
            (1, "Ran importer@2/import; Wrote stage `story`: passed", "Ran the family's importer, which hardcodes the cast."),
            (step.Sequence, step.Headline, step.Brief));
        Assert.Equal(["ScriptRan", "ArtifactWritten"], step.Calls[0].Effects.Select(e => e.Kind));
        Assert.Null(rebriefing.Brief);
        Assert.Equal("", rebriefing.Steps[0].Brief);
        Assert.Equal(StatusCode.NotFound, missing.StatusCode);
    }

    [Fact]
    public async Task Theplots_operator_registers_a_family_describes_it_and_resets_it()
    {
        var family = $"memo-{Guid.NewGuid():N}"[..30];

        await _theplot.Call<TheplotFamiliesService, CreateFamilyReply>((s, c) => s.CreateFamily(
            new CreateFamilyRequest { Family = family, DisplayName = " Memos ", Description = "Internal memos." }, c));
        var duplicate = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotFamiliesService, CreateFamilyReply>(
            (s, c) => s.CreateFamily(new CreateFamilyRequest { Family = family, DisplayName = "Memos" }, c)));
        await _theplot.Call<TheplotFamiliesService, UpdateFamilyReply>((s, c) => s.UpdateFamily(
            new UpdateFamilyRequest { Family = family, DisplayName = "Office memos", Description = "Internal memos." }, c));
        var reset = await _theplot.Call<TheplotFamiliesService, ResetFamilyReply>(
            (s, c) => s.ResetFamily(new ResetFamilyRequest { Family = family }, c));
        var detail = await _theplot.Call<TheplotFamiliesService, GetFamilyReply>(
            (s, c) => s.GetFamily(new GetFamilyRequest { Family = family }, c));
        var listed = await _theplot.Call<TheplotFamiliesService, ListFamiliesReply>(
            (s, c) => s.ListFamilies(new ListFamiliesRequest(), c));

        Assert.Equal(StatusCode.AlreadyExists, duplicate.StatusCode);
        Assert.Equal(1, reset.Generation);
        Assert.Equal(("Office memos", 1, RunMode.Discovery), (detail.Info.DisplayName, detail.CurrentGeneration, detail.Mode));
        Assert.StartsWith("operator-", detail.Info.CreatedBy);
        Assert.Contains(listed.Families, f => f.Family == family && f.Generation == 1);
    }

    [Fact]
    public async Task A_family_name_that_cannot_be_a_key_is_refused_and_an_unknown_one_is_not_found()
    {
        var invalid = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotFamiliesService, CreateFamilyReply>(
            (s, c) => s.CreateFamily(new CreateFamilyRequest { Family = "Memos#1", DisplayName = "Memos" }, c)));
        var missing = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotFamiliesService, GetFamilyReply>(
            (s, c) => s.GetFamily(new GetFamilyRequest { Family = $"never-{Guid.NewGuid():N}"[..30] }, c)));
        var reset = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotFamiliesService, ResetFamilyReply>(
            (s, c) => s.ResetFamily(new ResetFamilyRequest { Family = $"never-{Guid.NewGuid():N}"[..30] }, c)));

        Assert.Equal(StatusCode.InvalidArgument, invalid.StatusCode);
        Assert.Equal(StatusCode.NotFound, missing.StatusCode);
        Assert.Equal(StatusCode.NotFound, reset.StatusCode);
    }

    [Fact]
    public async Task A_missing_artifact_or_executor_is_not_found()
    {
        var artifact = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotRunsService, GetArtifactReply>(
            (s, c) => s.GetArtifact(new GetArtifactRequest
            {
                Artifact = new ArtifactRef { RunId = "run", StageId = "$input", Hash = new string('a', 64) },
            }, c)));
        var executor = await Assert.ThrowsAsync<RpcException>(() => _theplot.Call<TheplotRunsService, GetExecutorReply>(
            (s, c) => s.GetExecutor(new GetExecutorRequest { Id = "nobody", Version = 1 }, c)));

        Assert.Equal(StatusCode.NotFound, artifact.StatusCode);
        Assert.Equal(StatusCode.NotFound, executor.StatusCode);
    }

    private async Task<string> OpenRunAsync(string family)
    {
        var runId = DocumentImportIds.New();
        await Ledger.OpenAsync(
            runId, new Engine.Workflows.DocumentSignature(family, new Dictionary<string, string>()), Engine.Policy.RunMode.Discovery, default);
        return runId;
    }

    private static Engine.Storage.StageRecord Attempt(string runId) =>
        new(
            runId,
            new Engine.Workflows.StageDefinition(
                "story", [], new Engine.Workflows.ContractRef("document-text", 1), new Engine.Workflows.ContractRef("story-draft", 1),
                ["story-draft-json"], Engine.Executors.Budget.Unbounded),
            [new Engine.Storage.ArtifactRef(runId, Engine.Storage.ArtifactRef.InputStageId, new string('b', 64))],
            new Engine.Executors.ExecutorRef("small", 1),
            Engine.Executors.Tier.DelegateSmall,
            new Engine.Executors.StageResult(
                new Engine.Storage.ArtifactRef(runId, "story", new string('c', 64)), null,
                new Engine.Executors.Cost(0.25m, TimeSpan.FromSeconds(2)), []),
            [new Engine.Verification.VerifierResult("story-draft-json", Engine.Verification.Verdict.Pass, "ok", [])],
            IsShadow: false);
}
