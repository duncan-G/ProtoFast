using System.Collections.Concurrent;
using ProtoFast.Api.Services.OffTheLoop;

namespace ProtoFast.Api.IntegrationTests;

/// <summary>The models each test user's runs get, since runs resolve theirs from the container.</summary>
public sealed class ScriptedModels
{
    private readonly ConcurrentDictionary<string, IResearchModel> _research = new();
    private readonly ConcurrentDictionary<string, IWritingModel> _writing = new();

    public void Use(string userId, IResearchModel model) => _research[userId] = model;

    public void Use(string userId, IWritingModel model) => _writing[userId] = model;

    public IResearchModel ResearchFor(string? userId) =>
        _research.TryGetValue(userId ?? "", out var model) ? model : new ScriptedResearchModel(null) { Fails = true };

    public IWritingModel WritingFor(string? userId) =>
        _writing.TryGetValue(userId ?? "", out var model) ? model : new ScriptedWritingModel { Fails = true };
}
