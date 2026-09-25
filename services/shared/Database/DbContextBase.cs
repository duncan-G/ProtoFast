using Microsoft.EntityFrameworkCore;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

/// <summary>
/// Base for every service context. Derived contexts describe their entities in
/// <see cref="ConfigureModel"/>; this class then attaches the per-user query filters over the
/// finished model, which is why <see cref="OnModelCreating"/> is sealed rather than overridable.
/// </summary>
public abstract class DbContextBase(DbContextOptions options, QueryFilterService queryFilterService, UserContext userContext)
    : DbContext(options), IDbContext
{
    /// <summary>
    /// The caller subject every scoped query and write is confined to. The query filters read it
    /// through this property so EF evaluates it per query; null fails closed.
    /// </summary>
    public string? CurrentUserId => userContext.CurrentUserId;

    protected sealed override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        ConfigureModel(modelBuilder);
        queryFilterService.AddUserFilters(modelBuilder, this);
    }

    protected abstract void ConfigureModel(ModelBuilder modelBuilder);
}
