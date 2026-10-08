using ProtoFast.DocumentImport.Core;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Storage;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Screenplay.Tagging;

namespace ProtoFast.DocumentImport.Worker.Import;

/// <summary>
/// Upload in, story row out: text, then the engine run, then the terminal artifact, its library
/// names tagged as mentions, saved in place of the document. Returns null when the document was
/// already imported or deleted. Each step is reported to the ledger under the upload id, which is
/// what the desk shows while it waits.
/// </summary>
public sealed class DocumentImportRunner(
    SourceTextResolver sourceText,
    IArtifactStore artifacts,
    RunDispatcher dispatcher,
    MentionTagger tagger,
    StoryWriter writer,
    IRunLedger ledger,
    ILogger<DocumentImportRunner> logger)
{
    public async Task<Guid?> RunAsync(DocumentImportRequested request, CancellationToken ct)
    {
        if (!await writer.IsPendingAsync(request.UserId, request.UploadId, ct))
        {
            return null;
        }

        await ledger.ReportAsync(request.UploadId, new RunProgress(RunPhase.Preparing), ct);
        var text = await sourceText.ResolveAsync(request, ct);

        // The upload id stands in for the run id of the input, so its artifact key names the upload.
        var input = await artifacts.PutAsync(
            request.UploadId, ArtifactRef.InputStageId, StoryJson.ToStream(text), StoryStages.SourceContract, ct);

        var summary = await dispatcher.RunAsync(input, ct);
        logger.LogInformation(
            "Run {RunId} for upload {UploadId} ({Family}) recorded {Stages} stage attempts",
            summary.RunId, request.UploadId, summary.DocumentSignature.Family, summary.Stages.Count);

        var story = summary.Stages.LastOrDefault(s => s.StageId == StoryStages.StoryStage && s is { IsShadow: false, Passed: true })
            ?? throw new InvalidOperationException($"Run {summary.RunId} produced no accepted story.");

        await ledger.ReportAsync(request.UploadId, new RunProgress(RunPhase.Finishing, summary.RunId), ct);
        var draft = StoryJson.Deserialize<StoryDraft>(await ArtifactText.ReadAsync(artifacts, story.Output, ct));
        draft = await tagger.TagAsync(draft, ct);
        var storyId = await writer.WriteAsync(request.UserId, request.UploadId, draft, ct);
        if (storyId is { } saved)
        {
            await ledger.ReportAsync(
                request.UploadId, new RunProgress(RunPhase.Finished, summary.RunId, ResultId: saved.ToString()), ct);
        }

        return storyId;
    }
}
