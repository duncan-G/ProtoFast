namespace ProtoFast.Database.Abstractions;

public sealed record PageResponse<TEntity>(
    IReadOnlyList<TEntity> Items,
    string? NextPageToken);
