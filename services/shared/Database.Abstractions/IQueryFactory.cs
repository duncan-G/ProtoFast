namespace ProtoFast.Database.Abstractions;

public interface IQueryFactory<in TEntity, out TQuery> where TQuery : IQuery<TEntity>
{
    TQuery Create();
}
