using ProtoFast.Auth.Api.Sessions;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>An in-memory session store — enough to exercise the resolve and sign-in paths without
/// a Redis server. Tests plant sessions with <see cref="Seed"/>; every other lookup misses.</summary>
internal sealed class StubSessionStore : ISessionStore
{
    private readonly Dictionary<string, SessionData> _sessions = new(StringComparer.Ordinal);

    public string Seed(SessionData data)
    {
        var id = SessionIds.Generate();
        _sessions[id] = data;
        return id;
    }

    public Task<string> CreateAsync(SessionData data, CancellationToken ct = default) =>
        Task.FromResult(Seed(data));

    public Task<SessionData?> GetAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult(_sessions.GetValueOrDefault(sessionId));

    public Task DeleteAsync(string sessionId, CancellationToken ct = default)
    {
        _sessions.Remove(sessionId);
        return Task.CompletedTask;
    }

    public Task DeleteByKeycloakSessionAsync(string realm, string kcSessionId, CancellationToken ct = default)
    {
        foreach (var (id, data) in _sessions.ToArray())
        {
            if (data.Realm == realm && data.KcSessionId == kcSessionId)
            {
                _sessions.Remove(id);
            }
        }

        return Task.CompletedTask;
    }

    public Task UpdateAsync(string sessionId, SessionData data, CancellationToken ct = default)
    {
        _sessions[sessionId] = data;
        return Task.CompletedTask;
    }

    public Task<string> ReplaceAsync(string oldSessionId, SessionData data, CancellationToken ct = default)
    {
        _sessions.Remove(oldSessionId);
        return Task.FromResult(Seed(data));
    }
}
