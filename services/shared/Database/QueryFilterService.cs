using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore.Metadata;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public sealed class QueryFilterService()
{
    public Expression<Func<TEntity, bool>>? GetUserFilter<TEntity>(IEntityType entityType, UserContext userContext) where TEntity : class
    {
        if (entityType.HasNoScope())
        {
            return null;
        }

        if (userContext.CurrentUserId == null)
        {
            return null;
        }

        // Check if entity directly has UserId
        if (entityType.FindProperty("UserId") != null)
        {
            return CreateDirectUserFilter<TEntity>(userContext);
        }

        // Find the shortest path to a user-scoped entity
        List<INavigation>? path = FindShortestPathToUserScoped(entityType);

        if (path == null)
        {
            throw new InvalidOperationException(
                $"Entity {entityType.Name} is not user-scoped and has no navigation path to a user-scoped entity");
        }

        return CreateNavigationUserFilter<TEntity>(userContext, path);
    }

    private Expression<Func<TEntity, bool>> CreateDirectUserFilter<TEntity>(UserContext userContext)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "e");
        MemberExpression userIdProperty = Expression.Property(parameter, "UserId");
        ConstantExpression userIdValue = Expression.Constant(userContext.CurrentUserId);
        BinaryExpression equals = Expression.Equal(userIdProperty, userIdValue);
        return Expression.Lambda<Func<TEntity, bool>>(equals, parameter);
    }

    private Expression<Func<TEntity, bool>> CreateNavigationUserFilter<TEntity>(UserContext userContext, List<INavigation> path)
    {
        ParameterExpression parameter = Expression.Parameter(typeof(TEntity), "e");
        Expression navigation = parameter;

        // Build the navigation chain
        foreach (INavigation nav in path)
        {
            navigation = Expression.Property(navigation, nav.Name);
        }

        // Add the UserId property at the end
        MemberExpression userIdProperty = Expression.Property(navigation, "user_id");
        ConstantExpression userIdValue = Expression.Constant(userContext.CurrentUserId);
        BinaryExpression equals = Expression.Equal(userIdProperty, userIdValue);

        return Expression.Lambda<Func<TEntity, bool>>(equals, parameter);
    }

    private List<INavigation>? FindShortestPathToUserScoped(IEntityType startEntity)
    {
        HashSet<IEntityType> visited = new();
        Queue<(IEntityType Entity, List<INavigation> Path)> queue = new();
        queue.Enqueue((startEntity, []));

        while (queue.Count > 0)
        {
            (IEntityType currentEntity, List<INavigation> currentPath) = queue.Dequeue();

            if (!visited.Add(currentEntity))
            {
                continue;
            }

            // Skip entities marked as having no scope
            if (currentEntity.HasNoScope())
            {
                continue;
            }

            // Check if current entity has UserId
            if (currentEntity.FindProperty("user_id") != null)
            {
                return currentPath;
            }

            // Add all unvisited navigations to queue
            foreach (INavigation navigation in currentEntity.GetNavigations())
            {
                if (!visited.Contains(navigation.TargetEntityType))
                {
                    List<INavigation> newPath = new(currentPath) { navigation };
                    queue.Enqueue((navigation.TargetEntityType, newPath));
                }
            }
        }

        return null;
    }
}
