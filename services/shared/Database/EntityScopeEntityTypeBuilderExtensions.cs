using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace ProtoFast.Database;

public static class EntityScopeEntityTypeBuilderExtensions
{
    private const string NoScopeAnnotationName = "NoScope";

    /// <summary>
    /// Opts an entity out of user scoping: no query filter is attached and writes are not
    /// owner-checked. For reference data that is genuinely shared between users.
    /// </summary>
    public static EntityTypeBuilder HasNoScope(this EntityTypeBuilder builder)
    {
        builder.Metadata.SetAnnotation(NoScopeAnnotationName, true);
        return builder;
    }

    public static bool HasNoScope(this IReadOnlyEntityType entityType) =>
        entityType.FindAnnotation(NoScopeAnnotationName)?.Value as bool? == true;
}
