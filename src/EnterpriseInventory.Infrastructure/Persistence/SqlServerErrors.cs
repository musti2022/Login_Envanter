using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace EnterpriseInventory.Infrastructure.Persistence;

internal static class SqlServerErrors
{
    // 2601: duplicate key in a unique index; 2627: violation of a unique or primary key constraint.
    private const int DuplicateKeyInUniqueIndex = 2601;
    private const int UniqueConstraintViolation = 2627;

    public static bool IsUniqueKeyViolation(this DbUpdateException exception) =>
        exception.InnerException is SqlException { Number: DuplicateKeyInUniqueIndex or UniqueConstraintViolation };

    /// <summary>A unique index or constraint with this name refused the change.</summary>
    public static bool IsUniqueKeyViolation(this DbUpdateException exception, string indexName) =>
        exception.IsUniqueKeyViolation()
        && ((SqlException)exception.InnerException!).Message.Contains($"'{indexName}'", StringComparison.Ordinal);
}
