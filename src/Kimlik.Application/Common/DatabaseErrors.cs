using System.Data.Common;
using Microsoft.EntityFrameworkCore;

namespace Kimlik.Application.Common;

internal static class DatabaseErrors
{
    private const string UniqueViolationSqlState = "23505";
    private const string ForeignKeyViolationSqlState = "23503";

    /// <summary>Whether a unique index rejected the change, typically because a concurrent request won a race.</summary>
    public static bool IsUniqueViolation(this DbUpdateException exception) =>
        exception.InnerException is DbException { SqlState: UniqueViolationSqlState };

    /// <summary>Whether a row the change refers to is gone, typically because a concurrent request deleted it.</summary>
    public static bool IsForeignKeyViolation(this DbUpdateException exception) =>
        exception.InnerException is DbException { SqlState: ForeignKeyViolationSqlState };
}
