using System.Diagnostics;
using Microsoft.EntityFrameworkCore.Storage;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public sealed class ReadWriteUnitOfWork : IUnitOfWork
{
    private readonly Activity? _activity;
    private readonly DbContextBase _internalDbContextBase;
    private IDbContextTransaction? _currentTransaction;

    public ReadWriteUnitOfWork(IDbContext dbContext, Activity? activity)
    {
        DbContext = dbContext;
        _internalDbContextBase = (DbContextBase)dbContext;
        _activity = activity;
        UnitOfWorkContext.Current = this;
    }

    public IDbContext DbContext { get; }

    public async Task SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        using Activity? activity =
            Activity.Current?.Source.StartActivity($"{GetType().Name}.{nameof(SaveChangesAsync)}", ActivityKind.Server);
        activity?.AddTag("name", GetType().Name);

        await _internalDbContextBase.SaveChangesAsync(cancellationToken);
    }

    public async Task CommitAsync(CancellationToken cancellationToken = default)
    {
        using Activity? activity =
            Activity.Current?.Source.StartActivity($"{GetType().Name}.{nameof(CommitAsync)}", ActivityKind.Server);
        try
        {
            await _internalDbContextBase.SaveChangesAsync(cancellationToken);

            if (_currentTransaction != null)
            {
                await _currentTransaction.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await RollbackAsync(cancellationToken);
            throw;
        }
        finally
        {
            if (_currentTransaction != null)
            {
                _currentTransaction.Dispose();
                _currentTransaction = null;
            }
        }
    }

    public void Dispose()
    {
        if (UnitOfWorkContext.Current == this)
        {
            UnitOfWorkContext.Current = null;
        }

        _currentTransaction?.Dispose();
        _activity?.Dispose();
    }

    public async Task EnsureTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_currentTransaction != null)
        {
            return;
        }

        using Activity? activity = _activity?.Source.StartActivity($"{GetType().Name}.{nameof(EnsureTransactionAsync)}",
            ActivityKind.Server);
        activity?.AddTag("name", GetType().Name);
        _currentTransaction = await _internalDbContextBase.Database.BeginTransactionAsync(cancellationToken);
    }

    private async Task RollbackAsync(CancellationToken cancellationToken = default)
    {
        using Activity? activity =
            _activity?.Source.StartActivity($"{GetType().Name}.{nameof(RollbackAsync)}", ActivityKind.Server);
        activity?.AddTag("name", GetType().Name);
        if (_currentTransaction != null)
        {
            await _currentTransaction.RollbackAsync(cancellationToken);
            _currentTransaction.Dispose();
            _currentTransaction = null;
        }
    }
}
