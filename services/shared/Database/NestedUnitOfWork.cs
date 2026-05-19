using System.Diagnostics;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public sealed class NestedUnitOfWork : IUnitOfWork
{
    private readonly Activity? _activity;
    private readonly DbContextBase _internalDbContextBase;
    private readonly bool _isReadWrite;

    internal NestedUnitOfWork(IUnitOfWork parent, bool isReadWrite, Activity? activity)
    {
        Parent = parent;
        _isReadWrite = isReadWrite;
        _activity = activity;
        DbContext = parent.DbContext;
        _internalDbContextBase = (DbContextBase)DbContext;
        UnitOfWorkContext.Current = this;
    }

    public IDbContext DbContext { get; }

    public bool IsReadWrite => _isReadWrite;

    internal IUnitOfWork Parent { get; }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        if (!_isReadWrite)
        {
            return;
        }

        using Activity? activity =
            Activity.Current?.Source.StartActivity($"{GetType().Name}.{nameof(SaveChangesAsync)}", ActivityKind.Server);

        await _internalDbContextBase.SaveChangesAsync(cancellationToken);
    }

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public void Dispose()
    {
        if (UnitOfWorkContext.Current == this)
        {
            UnitOfWorkContext.Current = Parent;
        }

        _activity?.Dispose();
    }

    internal async Task EnsureTransactionAsync(CancellationToken cancellationToken = default)
    {
        ReadWriteUnitOfWork root = GetRootReadWriteUnitOfWork();
        await root.EnsureTransactionAsync(cancellationToken);
    }

    internal ReadWriteUnitOfWork GetRootReadWriteUnitOfWork()
    {
        IUnitOfWork current = Parent;
        while (current is NestedUnitOfWork nested)
        {
            current = nested.Parent;
        }

        return current as ReadWriteUnitOfWork
               ?? throw new InvalidOperationException(
                   "Cannot find a root ReadWriteUnitOfWork in the parent chain.");
    }
}
