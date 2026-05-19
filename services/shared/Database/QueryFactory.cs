using Microsoft.Extensions.DependencyInjection;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public sealed class QueryFactory<TEntity, TQuery>(IServiceProvider serviceProvider) : IQueryFactory<TEntity, TQuery>
    where TQuery : IQuery<TEntity>
{
    public TQuery Create() => ActivatorUtilities.CreateInstance<TQuery>(serviceProvider);
}
