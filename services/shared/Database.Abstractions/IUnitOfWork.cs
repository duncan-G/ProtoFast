namespace ProtoFast.Database.Abstractions;

public interface IUnitOfWork : IDisposable
{
    IDbContext DbContext { get; }

    Task SaveChangesAsync(CancellationToken cancellationToken = default);

    Task CommitAsync(CancellationToken cancellationToken = default);
}
