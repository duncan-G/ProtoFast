using System.Collections.Concurrent;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>
/// A model that answers from a script, kept per user so parallel tests do not read each other's
/// replies. A test enqueues what the model will say before it calls the service.
/// </summary>
public sealed class ScriptedLanguageModelFactory : ILanguageModelFactory, ILanguageModel
{
    /// <summary>Who is asking; the factory is a singleton, so the writer sets it for the call.</summary>
    public static readonly AsyncLocal<string?> User = new();

    private readonly ConcurrentDictionary<string, ConcurrentQueue<string>> _replies = new();
    private readonly ConcurrentDictionary<string, List<string>> _prompts = new();

    public string ModelId => "scripted";

    public ILanguageModel For(string modelClass) => this;

    public ILanguageModel For(string modelClass, string provider) => this;

    public void Enqueue(string userId, params string[] replies)
    {
        var queue = _replies.GetOrAdd(userId, _ => new ConcurrentQueue<string>());
        foreach (var reply in replies)
        {
            queue.Enqueue(reply);
        }
    }

    public IReadOnlyList<string> Prompts(string userId) => _prompts.TryGetValue(userId, out var prompts) ? prompts : [];

    public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var userId = User.Value ?? throw new InvalidOperationException("No user asked the model.");
        _prompts.GetOrAdd(userId, _ => []).Add(user);
        if (!_replies.TryGetValue(userId, out var queue) || !queue.TryDequeue(out var reply))
        {
            throw new InvalidOperationException("The test scripted no reply for this call.");
        }

        return Task.FromResult(new LanguageModelReply(reply, ModelId, 0, 0, 0));
    }

    public Task<LanguageModelReply> ConverseAsync(
        string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
        throw new NotSupportedException();
}
