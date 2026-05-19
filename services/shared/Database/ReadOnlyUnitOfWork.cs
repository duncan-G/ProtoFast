using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public sealed class ReadOnlyUnitOfWork : IUnitOfWork
{
    private readonly Activity? _activity;
    private readonly DbContextBase _internalDbContext;

    public ReadOnlyUnitOfWork(IDbContext dbContext, Activity? activity)
    {
        DbContext = dbContext;
        _internalDbContext = (DbContextBase)dbContext;
        _activity = activity;
        UnitOfWorkContext.Current = this;
    }

    public IDbContext DbContext { get; }

    public void Dispose()
    {
        if (UnitOfWorkContext.Current == this)
        {
            UnitOfWorkContext.Current = null;
        }

        _activity?.Dispose();
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task CommitAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

    public DbSet<TEntity> DbSet<TEntity>() where TEntity : class => _internalDbContext.Set<TEntity>();
}
