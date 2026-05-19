using System.Linq.Expressions;
using System.Text.Json.Serialization;

namespace ProtoFast.Database.Abstractions;

public record PagingCursor<TSort, TKey>(TSort LastSortKeyValue, TKey LastPrimaryKeyValue);

public abstract record PagingTokenBase;

public abstract record PagingToken<TEntity, TSort, TKey>(PagingCursor<TSort, TKey>? Cursor, int PageSize)
    : PagingTokenBase
{
    [JsonIgnore] public abstract Expression<Func<TEntity, TSort>> SortKeySelector { get; }

    [JsonIgnore] public abstract Expression<Func<TEntity, TKey>> PrimaryKeySelector { get; }

    public override string ToString() =>
        $"PageSize: {PageSize}, PrimaryKey: {Cursor?.LastPrimaryKeyValue?.ToString()} SortKey: {Cursor?.LastSortKeyValue?.ToString()}, ";
}
