using System.Data.Common;
using System.Globalization;

namespace Omni.App.Persistence.Helpers;

/// <summary>
/// Caps how many database connections the app opens at once (QA t3-inbox-02). Supabase's session pooler allows a fixed number
/// of clients per database user (pool_size, 15 on the current plan) shared by the app, the admin portal and any tool; with
/// Npgsql's default of 100, a burst of webhooks opened more than that and the pooler refused them with "max clients reached"
/// (HTTP 500 for every user at that moment). Capped below the pooler's limit, a burst waits in the app for a free connection
/// (up to the connection string's Timeout, 15 s by default) instead of failing. A bigger database raises pool_size; raise
/// Database:MaxPoolSize with it, keeping the app's and the portal's caps together below it.
/// </summary>
public static class ConnectionPool
{
    private static readonly string[] MaxPoolSizeKeys = ["Maximum Pool Size", "Max Pool Size", "MaxPoolSize"];

    /// <summary>The connection string with <c>Maximum Pool Size</c> set to <paramref name="maxPoolSize"/>, unless it already
    /// sets one (an explicit value in the connection string wins) or no cap is configured.</summary>
    public static string WithPoolLimit(string connectionString, int? maxPoolSize)
    {
        if (maxPoolSize is not int max) return connectionString;
        if (max < 1) throw new InvalidOperationException("Database:MaxPoolSize must be at least 1.");

        // Parsed only to look for an existing cap; the string itself is kept as written.
        var parsed = new DbConnectionStringBuilder { ConnectionString = connectionString };
        if (MaxPoolSizeKeys.Any(parsed.ContainsKey)) return connectionString;
        return connectionString.TrimEnd().TrimEnd(';') + ";Maximum Pool Size=" + max.ToString(CultureInfo.InvariantCulture);
    }
}
