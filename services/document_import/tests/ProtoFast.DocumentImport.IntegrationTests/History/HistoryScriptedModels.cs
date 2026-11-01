using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.IntegrationTests.History;

/// <summary>Answers by model class: conversation turns in order, single completions by a function of the prompt.</summary>
public sealed class HistoryScriptedModels : ILanguageModelFactory
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<Func<IReadOnlyList<ChatMessage>, LanguageModelReply>>> _turns = new();
    private readonly ConcurrentDictionary<string, Func<string, string>> _completions = new();

    public ConcurrentQueue<(string ModelClass, string User)> Completions { get; } = new();

    public void Turns(string modelClass, params Func<IReadOnlyList<ChatMessage>, LanguageModelReply>[] turns)
    {
        var queue = _turns.GetOrAdd(modelClass, _ => new ConcurrentQueue<Func<IReadOnlyList<ChatMessage>, LanguageModelReply>>());
        foreach (var turn in turns)
        {
            queue.Enqueue(turn);
        }
    }

    public void Complete(string modelClass, Func<string, string> reply) => _completions[modelClass] = reply;

    public ILanguageModel For(string modelClass) => new Model(this, modelClass);

    public ILanguageModel For(string modelClass, string provider) => new Model(this, modelClass);

    private sealed class Model(HistoryScriptedModels owner, string modelClass) : ILanguageModel
    {
        public string ModelId => $"scripted-{modelClass}";

        public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
        {
            owner.Completions.Enqueue((modelClass, user));
            var reply = owner._completions.TryGetValue(modelClass, out var scripted)
                ? scripted(user)
                : throw new InvalidOperationException($"No completion scripted for the {modelClass} model.");
            return Task.FromResult(new LanguageModelReply(reply, ModelId, 1000, 500, 0.05m));
        }

        public Task<LanguageModelReply> ConverseAsync(
            string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
            owner._turns.TryGetValue(modelClass, out var queue) && queue.TryDequeue(out var turn)
                ? Task.FromResult(turn(messages))
                : throw new InvalidOperationException($"No turn left for the {modelClass} model.");
    }
}
