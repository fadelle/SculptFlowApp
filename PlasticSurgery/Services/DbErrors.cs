using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace PlasticSurgery.Services;

/// <summary>Small helpers for recognizing Postgres errors that mean "someone else got there first" —
/// used where a unique index (not application code) is the final arbiter of idempotency.</summary>
internal static class DbErrors
{
    public static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
