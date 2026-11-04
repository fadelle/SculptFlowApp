using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Omni.App.Persistence.Helpers;

/// <summary>Small helpers for recognizing Postgres errors that mean "someone else got there first" —
/// used where a unique index (not application code) is the final arbiter of idempotency.</summary>
internal static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };

    /// <summary>A reference to a row that doesn't exist (23503).</summary>
    public static bool IsForeignKeyViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation };

    /// <summary>A value the table forbids: a check constraint, a not-null column, text longer than its column or a number out of
    /// range (23514, 23502, 22001, 22003).</summary>
    public static bool IsInvalidValue(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.CheckViolation or PostgresErrorCodes.NotNullViolation
            or PostgresErrorCodes.StringDataRightTruncation or PostgresErrorCodes.NumericValueOutOfRange };

    /// <summary>The domain exception for a save the data itself made fail (QA t1-accounts-02), or null for any other failure.</summary>
    public static Exception? AsInvalidRecord(DbUpdateException ex) =>
        IsForeignKeyViolation(ex) ? new Common.Exceptions.InvalidRecordException(Common.Exceptions.InvalidRecordException.MissingReferenceKey, ex)
        : IsInvalidValue(ex) ? new Common.Exceptions.InvalidRecordException(Common.Exceptions.InvalidRecordException.InvalidValueKey, ex)
        : null;
}
