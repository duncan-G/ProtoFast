using ProtoFast.DocumentImport.History.Runs;
using ProtoFast.DocumentImport.Screenplay.Tagging;
using ProtoFast.DocumentImport.Worker.Import;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <summary>
/// One run: research a moment into a brief, write it as a screenplay, tag its names and save it as
/// a new story of the writer's. A retried run keeps the brief it already has.
/// </summary>
public sealed class HistoricalStoryRunner(
    HistoryResearcher researcher,
    HistoricalScreenwriter screenwriter,
    MentionTagger tagger,
    StoryWriter writer,
    StoryRunLedger ledger)
{
    public async Task RunAsync(StoryRun run, CancellationToken ct)
    {
        ResearchBrief brief;
        if (run.BriefJson is { } saved)
        {
            brief = BriefJson.Deserialize(saved);
        }
        else
        {
            await ledger.ProgressAsync(
                run.Id, StoryRunStatus.Researching, run.Request is null ? "Picking a moment" : "Researching the request", ct);
            brief = await researcher.ResearchAsync(run, ct);
            await ledger.SetBriefAsync(run.Id, brief.Title, BriefJson.Serialize(brief), ct);
        }

        await ledger.ProgressAsync(run.Id, StoryRunStatus.Writing, $"Writing {brief.Title}", ct);
        var draft = await screenwriter.WriteAsync(run, brief, ct);

        await ledger.ProgressAsync(run.Id, StoryRunStatus.Saving, "Tagging names", ct);
        draft = await tagger.TagAsync(draft, ct);
        ct.ThrowIfCancellationRequested();
        var storyId = await writer.CreateAsync(run.WriterId, draft, ct);

        var scenes = (draft.Containers ?? []).Sum(c => c.Scenes?.Count ?? 0);
        await ledger.AddEventAsync(run.Id, "saved", $"Saved {draft.Title} with {scenes} scenes", storyId.ToString(), 0, ct);
        await ledger.FinishAsync(run.Id, storyId, draft.Title, ct);
    }
}
