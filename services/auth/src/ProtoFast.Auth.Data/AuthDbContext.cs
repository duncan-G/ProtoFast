using Microsoft.EntityFrameworkCore;
using ProtoFast.Auth.Data.Entities;

namespace ProtoFast.Auth.Data;

/// <summary>
/// The <c>auth</c> database context. Deliberately a plain <see cref="DbContext"/> with a
/// <see cref="DbContextOptions{TContext}"/> constructor so EF tooling, the schema-migrations
/// runner, and the service all resolve it identically (guide §3.5.1). It does not pull in the
/// shared <c>AddCoreDatabaseServices</c> helper, which hard-wires pgvector that this Postgres
/// does not have.
/// </summary>
public sealed class AuthDbContext(DbContextOptions<AuthDbContext> options) : DbContext(options)
{
    public DbSet<UserAccount> Users => Set<UserAccount>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<UserAccount>(entity =>
        {
            entity.HasKey(u => u.Id);
            entity.Property(u => u.Realm).IsRequired().HasMaxLength(128);
            entity.Property(u => u.Subject).IsRequired().HasMaxLength(255);
            entity.Property(u => u.Email).IsRequired().HasMaxLength(320);

            // One identity per (realm, Keycloak subject) — the upsert key for
            // first-login provisioning, and what keeps realms isolated in the mirror.
            entity.HasIndex(u => new { u.Realm, u.Subject }).IsUnique();
        });
    }
}
