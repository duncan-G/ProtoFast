using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace ProtoFast.Database;

/// <summary>
/// Attaches the per-user query filter to every entity in a model. An entity is user-scoped when
/// it has a string <c>UserId</c> property holding the caller subject, or a path of required
/// foreign keys up to an owning entity that does. Anything else must opt out with <c>HasNoScope()</c>; the model build fails
/// otherwise, so a table can never be added without deciding who may see it.
/// </summary>
public sealed class QueryFilterService
{
    public const string UserIdPropertyName = "UserId";

    public void AddUserFilters(ModelBuilder modelBuilder, DbContextBase dbContext)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            if (entityType.IsOwned())
            {
                continue; // owned types take the filter of their owner
            }

            if (GetUserFilter(entityType, dbContext) is { } filter)
            {
                modelBuilder.Entity(entityType.ClrType).HasQueryFilter(filter);
            }
        }
    }

    public LambdaExpression? GetUserFilter(IReadOnlyEntityType entityType, DbContextBase dbContext)
    {
        if (entityType.HasNoScope())
        {
            return null;
        }

        List<IReadOnlyNavigation> path = FindShortestPathToUserScoped(entityType)
                                         ?? throw new InvalidOperationException(
                                             $"Entity {entityType.DisplayName()} has no {UserIdPropertyName} and no navigation path to an entity that does. " +
                                             "Add one, or mark the entity HasNoScope() if it is genuinely shared.");

        ParameterExpression parameter = Expression.Parameter(entityType.ClrType, "e");
        Expression owner = parameter;
        foreach (IReadOnlyNavigation navigation in path)
        {
            owner = Expression.Property(owner, navigation.Name);
        }

        MemberExpression userId = Expression.Property(owner, UserIdPropertyName);
        if (userId.Type != typeof(string))
        {
            throw new InvalidOperationException(
                $"{userId.Member.DeclaringType?.Name}.{UserIdPropertyName} must be a string: it holds the caller subject from the internal JWT.");
        }

        // Read the caller through the context instance rather than capturing its value: EF swaps
        // the captured context for the one running the query and turns the member access into a
        // parameter, so the filter follows the current call instead of freezing the first user
        // seen into the cached model. A null user compares equal to nothing, which fails closed.
        MemberExpression currentUserId = Expression.Property(
            Expression.Constant(dbContext, dbContext.GetType()),
            nameof(DbContextBase.CurrentUserId));

        return Expression.Lambda(Expression.Equal(userId, currentUserId), parameter);
    }

    private static List<IReadOnlyNavigation>? FindShortestPathToUserScoped(IReadOnlyEntityType startEntity)
    {
        HashSet<IReadOnlyEntityType> visited = [];
        Queue<(IReadOnlyEntityType Entity, List<IReadOnlyNavigation> Path)> queue = new();
        queue.Enqueue((startEntity, []));

        while (queue.Count > 0)
        {
            (IReadOnlyEntityType currentEntity, List<IReadOnlyNavigation> currentPath) = queue.Dequeue();

            if (!visited.Add(currentEntity) || currentEntity.HasNoScope())
            {
                continue;
            }

            if (currentEntity.FindProperty(UserIdPropertyName) != null)
            {
                return currentPath;
            }

            foreach (IReadOnlyNavigation navigation in currentEntity.GetNavigations())
            {
                if (IsPathToOwner(navigation) && !visited.Contains(navigation.TargetEntityType))
                {
                    queue.Enqueue((navigation.TargetEntityType, [.. currentPath, navigation]));
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Only a required foreign key reaches an owner from every row. A filter through an optional
    /// reference would hide the rows where it is null.
    /// </summary>
    private static bool IsPathToOwner(IReadOnlyNavigation navigation) =>
        !navigation.IsCollection && navigation.IsOnDependent && navigation.ForeignKey.IsRequired;
}
