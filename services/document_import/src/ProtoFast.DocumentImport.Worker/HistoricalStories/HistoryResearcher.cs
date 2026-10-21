using System.Text.Json;
using Microsoft.Extensions.Options;
using ProtoFast.DocumentImport.History.Corpus;
using ProtoFast.DocumentImport.History.Runs;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Worker.HistoricalStories;

/// <summary>
/// The research desk: a model with the corpus tools works until it submits a brief whose every
/// citation is a passage it read. Each turn and tool call goes on the run's trail with its cost.
/// </summary>
public sealed class HistoryResearcher(
    ILanguageModelFactory models,
    HistoryCorpus corpus,
    StoryRunLedger ledger,
    IOptions<HistoricalStoryOptions> options,
    ILogger<HistoryResearcher> logger)
{
    private const int MaxNudges = 2;
    private const int WarnWhenTurnsLeft = 4;

    public async Task<ResearchBrief> ResearchAsync(StoryRun run, CancellationToken ct)
    {
        var settings = options.Value;
        var model = settings.Provider is { Length: > 0 } provider
            ? models.For(settings.ResearchModelClass, provider)
            : models.For(settings.ResearchModelClass);
        var tools = new ResearchTools(corpus);
        var request = run.Request is { Length: > 0 } asked ? HistoryPrompts.Requested(asked) : HistoryPrompts.RandomRequest;
        var messages = new List<ChatMessage> { ChatMessage.User(request) };
        var nudges = 0;

        for (var turn = 1; turn <= settings.MaxResearchTurns; turn++)
        {
            var reply = await model.ConverseAsync(HistoryPrompts.ResearchSystem, messages, ResearchTools.Definitions, ct);
            var calls = reply.ToolCalls ?? [];
            messages.Add(ChatMessage.Assistant(reply.Text, calls));
            var cost = reply.Cost;
            if (!string.IsNullOrWhiteSpace(reply.Text))
            {
                await ledger.AddEventAsync(run.Id, "note", Line(reply.Text), reply.Text, cost, ct);
                cost = 0;
            }

            if (calls.Count == 0)
            {
                if (cost > 0)
                {
                    await ledger.AddEventAsync(run.Id, "note", "(no reply text)", null, cost, ct);
                }

                if (++nudges > MaxNudges)
                {
                    throw new InvalidOperationException("The research stopped without submitting a brief.");
                }

                messages.Add(ChatMessage.User("Keep researching with the tools, and submit the brief with submit_brief when it is ready."));
                continue;
            }

            var results = new List<ToolResult>();
            foreach (var call in calls)
            {
                if (call.Name == ResearchTools.SubmitBrief)
                {
                    var (brief, problems) = Submitted(call.Input, tools.ReadPassages);
                    await ledger.AddEventAsync(
                        run.Id, "brief", problems.Count == 0 ? $"Brief: {brief!.Title}" : "Brief sent back: " + string.Join(" ", problems),
                        call.Input.GetRawText(), cost, ct);
                    cost = 0;
                    if (brief is not null && problems.Count == 0)
                    {
                        return brief;
                    }

                    results.Add(new ToolResult(call.Id, call.Name, "The brief is not ready:\n- " + string.Join("\n- ", problems), true));
                    continue;
                }

                ToolOutcome outcome;
                try
                {
                    outcome = await tools.RunAsync(call, ct);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    logger.LogWarning(e, "Tool {Tool} failed in story run {RunId}", call.Name, run.Id);
                    outcome = ToolOutcome.Error($"{call.Name} failed: {e.Message}");
                }

                await ledger.AddEventAsync(run.Id, call.Name, outcome.Summary, outcome.Content, cost, ct);
                await ledger.ProgressAsync(run.Id, StoryRunStatus.Researching, Line(outcome.Summary), ct);
                cost = 0;
                results.Add(new ToolResult(call.Id, call.Name, outcome.Content, outcome.IsError));
            }

            var left = settings.MaxResearchTurns - turn;
            if (left <= WarnWhenTurnsLeft && results.Count > 0)
            {
                var last = results[^1];
                results[^1] = last with
                {
                    Content = last.Content + $"\n\n[{left} turns left: submit the brief with what you have read.]",
                };
            }

            messages.Add(ChatMessage.Results(results));
        }

        throw new InvalidOperationException($"The research used its {settings.MaxResearchTurns} turns without an accepted brief.");
    }

    private static (ResearchBrief? Brief, IReadOnlyList<string> Problems) Submitted(JsonElement input, IReadOnlySet<long> read)
    {
        try
        {
            var brief = BriefJson.Deserialize(input);
            return (brief, BriefJson.Problems(brief, read));
        }
        catch (JsonException e)
        {
            return (null, [$"The brief does not match the tool's schema: {e.Message}"]);
        }
    }

    private static string Line(string text)
    {
        var line = text.Trim().Split('\n', 2)[0];
        return line.Length > 200 ? line[..200] + "…" : line;
    }
}
