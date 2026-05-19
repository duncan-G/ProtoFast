using Microsoft.EntityFrameworkCore;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public abstract class DbContextBase(DbContextOptions options, QueryFilterService queryFilterService, UserContext userContext)
    : DbContext(options), IDbContext
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        QueryFilterHelper.AddQueryFilters(modelBuilder, queryFilterService, userContext);
    }
}
