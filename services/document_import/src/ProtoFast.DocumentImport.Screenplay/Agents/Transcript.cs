using System.Text.Json;
using ProtoFast.DocumentImport.Engine.Discovery;
using ProtoFast.DocumentImport.Engine.Executors;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.Screenplay.Agents;

/// <summary>
/// The loop's conversation, journaled message by message as it grows so an interrupted run picks
/// up where it stopped: with the same messages, the same turn count, and any tool calls the model
/// made that never got their results.
/// </summary>
internal sealed class Transcript
{
    private readonly IAgentTools _tools;
    private readonly List<ChatMessage> _messages;

    private Transcript(IAgentTools tools, List<ChatMessage> messages, Cost spent)
    {
        _tools = tools;
        _messages = messages;
        Spent = spent;
    }

    public IReadOnlyList<ChatMessage> Messages => _messages;

    /// <summary>True when the journal already held messages, i.e. this is a resumed run.</summary>
    public bool Resumed { get; private init; }

    /// <summary>Model turns so far, which is what the turn budget counts.</summary>
    public int Turns => _messages.Count(m => m.Role == ChatRole.Assistant);

    public int Nudges => _messages.Count(m => m.Role == ChatRole.User && m.Text?.EndsWith(NudgeSuffix, StringComparison.Ordinal) == true);

    /// <summary>Everything the journaled model turns cost, whether or not a write has carried it yet.</summary>
    public Cost Spent { get; private set; }

    /// <summary>Tool calls at the tail of the conversation whose results were never journaled.</summary>
    public IReadOnlyList<ToolCall> PendingCalls =>
        _messages.Count > 0 && _messages[^1] is { Role: ChatRole.Assistant, ToolCalls.Count: > 0 } last ? last.ToolCalls : [];

    public static async Task<Transcript> OpenAsync(IAgentTools tools, string task)
    {
        List<TranscriptEntry> journaled;
        try
        {
            journaled = (await tools.LoadTranscript()).Select(TranscriptJson.Deserialize).ToList();
        }
        catch (JsonException e)
        {
            // Journaled under an older shape; retrying would read the same bytes.
            throw new DiscoveryFailedException("The run's journal could not be read, so it cannot be resumed.", e);
        }

        if (journaled.Count > 0)
        {
            var spent = journaled.Aggregate(Cost.Zero, (sum, e) => e.Spent is { } s ? new Cost(sum.Amount + s.Amount, sum.Duration + s.Duration) : sum);
            return new Transcript(tools, journaled.Select(e => e.Message).ToList(), spent) { Resumed = true };
        }

        var transcript = new Transcript(tools, [], Cost.Zero);
        await transcript.AddAsync(ChatMessage.User(task));
        return transcript;
    }

    public async Task AddAsync(ChatMessage message, Cost? spent = null)
    {
        await _tools.AppendTranscript(_messages.Count, TranscriptJson.Serialize(new TranscriptEntry(message, spent)));
        _messages.Add(message);
        if (spent is not null)
        {
            Spent = new Cost(Spent.Amount + spent.Amount, Spent.Duration + spent.Duration);
        }
    }

    public static string Nudge(string deliverable) => $"Stage `{deliverable}` {NudgeSuffix}";

    private const string NudgeSuffix = "has not passed its verifiers yet. Keep going until it has.";
}
