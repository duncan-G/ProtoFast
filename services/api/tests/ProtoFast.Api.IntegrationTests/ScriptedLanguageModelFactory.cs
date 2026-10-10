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
    private readonly ConcurrentDictionary<string, List<IReadOnlyList<string>>> _pictures = new();

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

    /// <summary>The captions of the pictures each call was shown, in call order; empty for a call shown none.</summary>
    public IReadOnlyList<IReadOnlyList<string>> Pictures(string userId) => _pictures.TryGetValue(userId, out var pictures) ? pictures : [];

    public Task<LanguageModelReply> CompleteAsync(string system, IReadOnlyList<ModelImage> images, string user, CancellationToken ct)
    {
        var userId = User.Value ?? throw new InvalidOperationException("No user asked the model.");
        lock (_pictures)
        {
            _pictures.GetOrAdd(userId, _ => []).Add(images.Select(i => i.Caption).ToList());
        }

        return CompleteAsync(system, user, ct);
    }

    public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
    {
        var userId = User.Value ?? throw new InvalidOperationException("No user asked the model.");
        // The frame verifiers ask in parallel.
        lock (_prompts)
        {
            _prompts.GetOrAdd(userId, _ => []).Add(user);
        }

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
