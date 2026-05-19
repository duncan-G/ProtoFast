using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public sealed class DateStampedSaveChangesInterceptor : SaveChangesInterceptor
{
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is { } context)
        {
            UpdateDateStampedFields(context);
        }

        return ValueTask.FromResult(result);
    }

    private static void UpdateDateStampedFields(DbContext context)
    {
        DateTime now = DateTime.UtcNow;
        foreach (EntityEntry<IDateStamped> entry in context.ChangeTracker.Entries<IDateStamped>())
        {
            if (entry.State == EntityState.Added)
            {
                entry.Entity.DateCreated = now;
                entry.Entity.DateLastModified = now;
            }

            if (entry.State == EntityState.Modified)
            {
                entry.Entity.DateLastModified = now;
            }
        }
    }
}
