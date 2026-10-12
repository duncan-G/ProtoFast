using System.Text.RegularExpressions;
using ProtoFast.DocumentImport.Screenplay.Models;

namespace ProtoFast.DocumentImport.IntegrationTests.WorldModel;

/// <summary>Answers each scene read with the reply scripted for the scene the prompt names, whichever order the reads come in.</summary>
public sealed partial class SceneScriptedModels : ILanguageModelFactory
{
    private readonly Dictionary<Guid, Func<string, string>> _replies = [];

    public List<(Guid SceneId, string Prompt)> Calls { get; } = [];

    public void Script(Guid sceneId, Func<string, string> reply) => _replies[sceneId] = reply;

    public ILanguageModel For(string modelClass) => new Model(this, modelClass);

    public ILanguageModel For(string modelClass, string provider) => new Model(this, $"{provider}/{modelClass}");

    [GeneratedRegex("<scene id=\"([0-9a-f-]{36})\"")]
    private static partial Regex SceneId();

    private sealed class Model(SceneScriptedModels owner, string modelClass) : ILanguageModel
    {
        public string ModelId => $"scripted-{modelClass}";

        public Task<LanguageModelReply> CompleteAsync(string system, string user, CancellationToken ct)
        {
            var sceneId = Guid.Parse(SceneId().Match(user).Groups[1].Value);
            lock (owner.Calls)
            {
                owner.Calls.Add((sceneId, user));
            }

            var reply = owner._replies.TryGetValue(sceneId, out var scripted)
                ? scripted(user)
                : throw new InvalidOperationException($"No reply scripted for scene {sceneId}.");
            return Task.FromResult(new LanguageModelReply(reply, ModelId, 1000, 200, 0.01m));
        }

        public Task<LanguageModelReply> ConverseAsync(
            string system, IReadOnlyList<ChatMessage> messages, IReadOnlyList<ToolDefinition> tools, CancellationToken ct) =>
            throw new NotSupportedException();
    }
}
