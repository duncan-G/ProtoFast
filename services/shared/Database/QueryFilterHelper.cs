using System.Linq.Expressions;
using System.Reflection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ProtoFast.Database.Abstractions;

namespace ProtoFast.Database;

public static class QueryFilterHelper
{
    public static void AddQueryFilters(ModelBuilder modelBuilder, QueryFilterService queryFilterService, UserContext userContext)
    {
        foreach (IMutableEntityType entityType in modelBuilder.Model.GetEntityTypes())
        {
            Type entityClrType = entityType.ClrType;
            MethodInfo? filterMethod = typeof(QueryFilterService)
                .GetMethod(nameof(QueryFilterService.GetUserFilter))?
                .MakeGenericMethod(entityClrType);

            if (filterMethod == null)
            {
                continue;
            }

            if (filterMethod.Invoke(queryFilterService, [entityType, userContext]) is LambdaExpression filter)
            {
                modelBuilder.Entity(entityClrType).HasQueryFilter(filter);
            }
        }
    }
}
