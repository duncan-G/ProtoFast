namespace ProtoFast.Database.Abstractions;

public sealed class UserContext
{
    private readonly AsyncLocal<Guid?> _currentUserId = new();

    public Guid? CurrentUserId
    {
        get => _currentUserId.Value;
        private set => _currentUserId.Value = value;
    }

    public IDisposable SetCurrentUser(Guid userId)
    {
        Guid? previous = CurrentUserId;
        CurrentUserId = userId;
        return new UserScope(this, previous);
    }

    private class UserScope(UserContext context, Guid? previousUserId) : IDisposable
    {
        public void Dispose() => context.CurrentUserId = previousUserId;
    }
}
