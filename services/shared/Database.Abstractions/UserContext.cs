namespace ProtoFast.Database.Abstractions;

/// <summary>
/// The user every scoped query and write is confined to: the internal JWT's <c>sub</c> (the
/// Keycloak subject, the same value the edge forwards as <c>x-user-id</c>). The transport sets it
/// once per call (the gRPC <c>UserContextInterceptor</c>); the shared query filters and the
/// user-scope save interceptor read it. Absent means fail closed: scoped queries match nothing
/// and scoped writes are refused.
/// </summary>
public sealed class UserContext
{
    private readonly AsyncLocal<string?> _currentUserId = new();

    public string? CurrentUserId
    {
        get => _currentUserId.Value;
        private set => _currentUserId.Value = value;
    }

    public IDisposable SetCurrentUser(string userId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);

        string? previous = CurrentUserId;
        CurrentUserId = userId;
        return new UserScope(this, previous);
    }

    private sealed class UserScope(UserContext context, string? previousUserId) : IDisposable
    {
        public void Dispose() => context.CurrentUserId = previousUserId;
    }
}
