using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProtoFast.Database;

/// <summary>
/// The write-side half of user scoping. The query filter keeps other users' rows out of reads,
/// but a detached entity handed to Add, Update or Remove never passes through a query, so this
/// checks it at save time: a new row is stamped with the caller (the only place an owner can
/// come from, since requests carry none), and any row whose owner is not the caller is refused.
/// Entities scoped through a navigation rather than their own <c>UserId</c> are covered by the
/// foreign key to a row the caller must already own.
/// </summary>
public sealed class UserScopeSaveChangesInterceptor : SaveChangesInterceptor
{
    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        ApplyUserScope(eventData.Context);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        ApplyUserScope(eventData.Context);
        return ValueTask.FromResult(result);
    }

    public static void ApplyUserScope(DbContext? context)
    {
        if (context is not DbContextBase dbContext)
        {
            return;
        }

        foreach (EntityEntry entry in dbContext.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
                || entry.Metadata.HasNoScope())
            {
                continue;
            }

            IProperty? userIdProperty = entry.Metadata.FindProperty(QueryFilterService.UserIdPropertyName);
            if (userIdProperty == null)
            {
                continue;
            }

            string currentUserId = dbContext.CurrentUserId
                                   ?? throw new InvalidOperationException(
                                       $"Cannot write {entry.Metadata.DisplayName()} without a current user.");

            PropertyEntry owner = entry.Property(userIdProperty.Name);
            if (entry.State == EntityState.Added && string.IsNullOrEmpty(owner.CurrentValue as string))
            {
                owner.CurrentValue = currentUserId;
            }
            else if (owner.CurrentValue as string != currentUserId)
            {
                throw new InvalidOperationException(
                    $"{entry.Metadata.DisplayName()} belongs to another user and cannot be written by the current one.");
            }
        }
    }
}
