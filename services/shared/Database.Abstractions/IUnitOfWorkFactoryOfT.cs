namespace ProtoFast.Database.Abstractions;

/// <summary>Opens units of work on <typeparamref name="TContext"/>, for a process holding more than one context.</summary>
public interface IUnitOfWorkFactory<TContext> : IUnitOfWorkFactory where TContext : IDbContext;
