using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using ProtoFast.DocumentImport.History.Corpus;
using ProtoFast.DocumentImport.History.Runs;
using ProtoFast.DocumentImport.Screenplay.Agents;
using ProtoFast.DocumentImport.Screenplay.Drafts;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <summary>
/// Writes the screenplay from the brief and the passages it cites, then sends it back once with
/// its problems when it is not well formed. A draft still flawed after that is saved when it has
/// scenes, since the story writer repairs what it can.
/// </summary>
public sealed class HistoricalScreenwriter(
    ILanguageModelFactory models,
    HistoryCorpus corpus,
    StoryRunLedger ledger,
    IOptions<HistoricalStoryOptions> options)
{
    public async Task<StoryDraft> WriteAsync(StoryRun run, ResearchBrief brief, CancellationToken ct)
    {
        var settings = options.Value;
        var model = settings.Provider is { Length: > 0 } provider
            ? models.For(settings.WritingModelClass, provider)
            : models.For(settings.WritingModelClass);
        var system = HistoryPrompts.WritingSystem(Math.Max(1, run.Scenes));
        var request = HistoryPrompts.WritingRequest(BriefJson.Serialize(brief), await SourcesAsync(brief, settings.MaxSourceChars, ct));

        var reply = await model.CompleteAsync(system, request, ct);
        var (draft, problems) = Parse(reply.Text);
        await ledger.AddEventAsync(run.Id, "draft", Summary(draft, problems), reply.Text, reply.Cost, ct);
        if (problems.Count == 0)
        {
            return draft!;
        }

        await ledger.ProgressAsync(run.Id, StoryRunStatus.Writing, "Fixing the screenplay", ct);
        var repair = $"{request}\n\nYour screenplay:\n\n<screenplay>\n{reply.Text}\n</screenplay>\n\n{HistoryPrompts.Repair("- " + string.Join("\n- ", problems))}";
        reply = await model.CompleteAsync(system, repair, ct);
        var (repaired, remaining) = Parse(reply.Text);
        await ledger.AddEventAsync(run.Id, "repair", Summary(repaired, remaining), reply.Text, reply.Cost, ct);

        var best = repaired ?? draft;
        return best is not null && (best.Containers ?? []).Any(c => c.Scenes is { Count: > 0 })
            ? best
            : throw new InvalidOperationException("The screenplay could not be read: " + string.Join(" ", remaining));
    }

    private async Task<string> SourcesAsync(ResearchBrief brief, int maxChars, CancellationToken ct)
    {
        var cited = brief.CitedPassages().ToList();
        var passages = (await corpus.GetAsync(cited, ct)).ToDictionary(p => p.Id);
        var text = new StringBuilder();
        foreach (var id in cited)
        {
            if (!passages.TryGetValue(id, out var passage))
            {
                continue;
            }

            var block = $"[{passage.Id}] {ResearchTools.Book(passage.Title, passage.Authors, passage.FirstYear, passage.LastYear)}\n{passage.Text}\n\n";
            if (text.Length + block.Length > maxChars)
            {
                break;
            }

            text.Append(block);
        }

        return text.ToString();
    }

    private static (StoryDraft? Draft, IReadOnlyList<string> Problems) Parse(string reply)
    {
        try
        {
            var draft = StoryJson.Deserialize<StoryDraft>(StoryJson.ExtractObject(reply));
            return (draft, DraftProblems.Find(draft));
        }
        catch (JsonException e)
        {
            return (null, [$"The reply is not the JSON object asked for: {e.Message}"]);
        }
    }

    private static string Summary(StoryDraft? draft, IReadOnlyList<string> problems)
    {
        if (draft is null)
        {
            return problems[0];
        }

        var scenes = (draft.Containers ?? []).Sum(c => c.Scenes?.Count ?? 0);
        var written = $"{draft.Title}: {scenes} scenes, {draft.Characters?.Count ?? 0} characters";
        return problems.Count == 0 ? written : $"{written}; {problems.Count} problems";
    }
}
