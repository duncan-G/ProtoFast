using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace ProtoFast.DocumentImport.Data.Postgres;

internal static class PostgresErrors
{
    public static bool IsUniqueViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    public static bool IsForeignKeyViolation(DbUpdateException e) =>
        e.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };
}
