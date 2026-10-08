using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.UnitTests.Screenplay;

/// <summary>Answers by model class; a script is consumed one reply per call, the last reply repeats.</summary>
internal sealed class ScriptedLanguageModelFactory : ILanguageModelFactory
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<string, string, string>>> _scripts = new();
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<IReadOnlyList<ChatMessage>, ChatMessage>>> _turns = new();

    public ConcurrentQueue<(string ModelClass, string System, string User)> Calls { get; } = new();

    /// <summary>Every conversation turn's messages, as the model received them.</summary>
    public ConcurrentQueue<(string System, IReadOnlyList<ChatMessage> Messages)> Turns { get; } = new();

    public void Script(string modelClass, params Func<string, string, string>[] replies)
    {
        var queue = _scripts.GetOrAdd(modelClass, _ => new ConcurrentQueue<Func<string, string, string>>());
        foreach (var reply in replies)
        {
            queue.Enqueue(reply);
        }
    }

    /// <summary>Each turn answers the conversation so far with an assistant message.</summary>
    public void ScriptTurns(string modelClass, params Func<IReadOnlyList<ChatMessage>, ChatMessage>[] turns)
    {
        var queue = _turns.GetOrAdd(modelClass, _ => new ConcurrentQueue<Func<IReadOnlyList<ChatMessage>, ChatMessage>>());
        foreach (var turn in turns)
        {
            queue.Enqueue(turn);
        }
    }

    public ILanguageModel For(string modelClass) => new ScriptedModel(modelClass, this);

    /// <summary>Scripted by <c>{provider}/{modelClass}</c>.</summary>
    public ILanguageModel For(string modelClass, string provider) => new ScriptedModel($"{provider}/{modelClass}", this);

    private static T Next<T>(ConcurrentDictionary<string, ConcurrentQueue<T>> scripts, string modelClass)
    {
        var queue = scripts.GetValueOrDefault(modelClass)
            ?? throw new InvalidOperationException($"No script for the {modelClass} model.");
        return (queue.Count > 1 ? queue.TryDequeue(out var reply) : queue.TryPeek(out reply))
            ? reply
            : throw new InvalidOperationException($"The {modelClass} script is empty.");
    }

    private sealed class ScriptedModel(string modelClass, ScriptedLanguageModelFactory owner) : ILanguageModel
    {
        public string ModelId => $"scripted-{modelClass}";

        public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
        {
            owner.Calls.Enqueue((modelClass, system, user));
            var reply = Next(owner._scripts, modelClass);
            return Task.FromResult(new LanguageModelReply(reply(system, user), ModelId, 100, 50, 0.01m));
        }

        public Task<LanguageModelReply> ConverseAsync(
            string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct)
        {
            var snapshot = messages.ToList();
            owner.Turns.Enqueue((system, snapshot));
            var turn = Next(owner._turns, modelClass)(snapshot);
            return Task.FromResult(new LanguageModelReply(
                turn.Text ?? "", ModelId, 100, 50, 0.01m,
                FinishReason: turn.ToolCalls.Count > 0 ? "tool_call" : "stop", ToolCalls: turn.ToolCalls));
        }
    }
}
