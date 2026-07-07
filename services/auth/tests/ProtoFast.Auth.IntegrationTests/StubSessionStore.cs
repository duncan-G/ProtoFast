using ProtoFast.Auth.Api.Sessions;

namespace ProtoFast.Auth.IntegrationTests;

/// <summary>An empty session store — every lookup misses. Enough to exercise the anonymous resolve
/// path and DI wiring without a Redis server.</summary>
internal sealed class StubSessionStore : ISessionStore
{
    public Task<string> CreateAsync(SessionData data, CancellationToken ct = default) =>
        Task.FromResult(SessionIds.Generate());

    public Task<SessionData?> GetAsync(string sessionId, CancellationToken ct = default) =>
        Task.FromResult<SessionData?>(null);

    public Task DeleteAsync(string sessionId, CancellationToken ct = default) => Task.CompletedTask;

    public Task UpdateAsync(string sessionId, SessionData data, CancellationToken ct = default) => Task.CompletedTask;

    public Task<string> ReplaceAsync(string oldSessionId, SessionData data, CancellationToken ct = default) =>
        Task.FromResult(SessionIds.Generate());
}
