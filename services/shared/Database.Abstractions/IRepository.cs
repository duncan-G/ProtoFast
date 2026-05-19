using System.Linq.Expressions;

namespace ProtoFast.Database.Abstractions;

public interface IRepository<TEntity, TKey> where TEntity : class
{
    Task<bool> ExistsByKeyAsync(TKey key, CancellationToken cancellationToken = default);

    Task<bool> ExistsByQueryAsync(IQuery<TEntity> query, CancellationToken cancellationToken = default);

    Task<TEntity?> GetByKeyAsync(TKey key, CancellationToken cancellationToken = default);

    Task<TEntity?> GetFirstByQueryAsync(IQuery<TEntity> query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TEntity>> GetByQueryAsync(IQuery<TEntity> query,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TResult>> GetByQueryAsync<TResult>(IQuery<TEntity> query,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default);

    Task<TResult?> GetFirstByQueryAsync<TResult>(IQuery<TEntity> query,
        Expression<Func<TEntity, TResult>> selector,
        CancellationToken cancellationToken = default);

    Task<PageResponse<TEntity>> GetByQueryPagedAsync<TSort>(
        IQuery<TEntity> query,
        PagingToken<TEntity, TSort, TKey> pagingToken,
        CancellationToken cancellationToken = default);

    Task AddAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task UpdateAsync(TEntity entity, CancellationToken cancellationToken = default);

    Task<int> UpdateByQueryAsync(
        IQuery<TEntity> query,
        Action<Microsoft.EntityFrameworkCore.Query.UpdateSettersBuilder<TEntity>> setPropertyCalls,
        CancellationToken cancellationToken = default);

    Task<int> DeleteByQueryAsync(IQuery<TEntity> query, CancellationToken cancellationToken = default);

    Task RemoveAsync(TEntity entity, CancellationToken cancellationToken = default);
}
