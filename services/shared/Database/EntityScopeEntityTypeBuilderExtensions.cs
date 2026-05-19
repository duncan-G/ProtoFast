using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProtoFast.Database;

public static class EntityScopeEntityTypeBuilderExtensions
{
    private const string NoScopeAnnotationName = "NoScope";

    public static EntityTypeBuilder HasNoScope(this EntityTypeBuilder builder)
    {
        builder.Metadata.SetAnnotation(NoScopeAnnotationName, true);
        return builder;
    }

    public static bool HasNoScope(this IEntityType entityType) =>
        entityType.FindAnnotation(NoScopeAnnotationName)?.Value as bool? == true;
}
