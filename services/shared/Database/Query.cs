using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

/// <summary>
/// Base for the per-entity query builders that <see cref="Repository{TEntity,TKey}"/> executes.
/// A query is a recorded specification: the fluent methods a subclass exposes only append steps
/// here, and the steps are replayed over the active unit of work's <see cref="DbSet{TEntity}"/>
/// when the repository asks for the queryable. Building lazily means a query can be composed
/// before the unit of work is opened and still run inside it.
/// </summary>
public abstract class Query<TEntity> : IExecutableQuery<TEntity> where TEntity : class
{
    private readonly List<Func<IQueryable<TEntity>, IQueryable<TEntity>>> _steps = [];

    private static DbSet<TEntity> DbSet
    {
        get
        {
            IUnitOfWork unitOfWork = UnitOfWorkContext.Current ??
                                     throw new InvalidOperationException(
                                         "No active unit of work found in the current context.");

            return ((DbContextBase)unitOfWork.DbContext).Set<TEntity>();
        }
    }

    IQueryable<TEntity> IExecutableQuery<TEntity>.AsQueryable()
    {
        IQueryable<TEntity> queryable = DbSet;
        foreach (Func<IQueryable<TEntity>, IQueryable<TEntity>> step in _steps)
        {
            queryable = step(queryable);
        }

        return queryable;
    }

    /// <summary>Appends a filter to the query.</summary>
    protected void Where(Expression<Func<TEntity, bool>> predicate) => Apply(q => q.Where(predicate));

    /// <summary>Appends an arbitrary LINQ step (ordering, includes, ...) to the query.</summary>
    protected void Apply(Func<IQueryable<TEntity>, IQueryable<TEntity>> step) => _steps.Add(step);
}
