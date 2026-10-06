using Npgsql;

namespace PlasticSurgery.Tests;

/// <summary>
/// The billing tests need a real PostgreSQL (row locks, unique indexes, the exclusion constraint and the immutability
/// triggers are part of what's being tested). Point SCULPTFLOW_TEST_DB at a THROWAWAY server you can create databases
/// on, e.g. "Host=localhost;Port=55433;Username=postgres". Each test run creates a fresh database
/// (sculptflow_test_xxx), applies Database/schema.sql to it, and drops it afterwards.
/// Without the variable the database tests are skipped. Remote hosts are refused unless
/// SCULPTFLOW_TEST_DB_ALLOW_REMOTE=1, so the tests can never touch the shared Supabase database by accident.
/// </summary>
public static class TestDatabase
{
    public const string EnvironmentVariable = "SCULPTFLOW_TEST_DB";

    public static string? AdminConnectionString
    {
        get
        {
            var value = Environment.GetEnvironmentVariable(EnvironmentVariable);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
    }

    public static string? SkipReason => AdminConnectionString is null
        ? $"Set {EnvironmentVariable} to a throwaway PostgreSQL server to run the database tests."
        : null;
}

/// <summary>A test that needs PostgreSQL: skipped (not passed) when SCULPTFLOW_TEST_DB isn't set.</summary>
public sealed class PostgresFactAttribute : FactAttribute
{
    public PostgresFactAttribute()
    {
        if (TestDatabase.SkipReason is { } reason) Skip = reason;
    }
}

/// <summary>A data-driven test that needs PostgreSQL: skipped when SCULPTFLOW_TEST_DB isn't set.</summary>
public sealed class PostgresTheoryAttribute : TheoryAttribute
{
    public PostgresTheoryAttribute()
    {
        if (TestDatabase.SkipReason is { } reason) Skip = reason;
    }
}

/// <summary>Creates the throwaway database once for the whole "Postgres" collection and drops it at the end.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    public string ConnectionString { get; private set; } = string.Empty;
    private string? _databaseName;

    public async Task InitializeAsync()
    {
        var admin = TestDatabase.AdminConnectionString;
        if (admin is null) return;

        var builder = new NpgsqlConnectionStringBuilder(admin);
        var host = builder.Host ?? string.Empty;
        if (host is not ("localhost" or "127.0.0.1" or "::1") && Environment.GetEnvironmentVariable("SCULPTFLOW_TEST_DB_ALLOW_REMOTE") != "1")
        {
            throw new InvalidOperationException($"Refusing to create test databases on '{host}'. Use a local throwaway server, or set SCULPTFLOW_TEST_DB_ALLOW_REMOTE=1.");
        }

        _databaseName = "sculptflow_test_" + Guid.NewGuid().ToString("N")[..12];
        await using (var connection = new NpgsqlConnection(admin))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"create database {_databaseName}", connection);
            await create.ExecuteNonQueryAsync();
        }

        builder.Database = _databaseName;
        builder.Pooling = true;
        builder.MaxPoolSize = 120;
        ConnectionString = builder.ConnectionString;

        await using var db = new NpgsqlConnection(ConnectionString);
        await db.OpenAsync();
        var script = await LoadSchemaAsync(db);
        await using var apply = new NpgsqlCommand(script, db);
        apply.CommandTimeout = 300;
        await apply.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        if (_databaseName is null || TestDatabase.AdminConnectionString is null) return;
        NpgsqlConnection.ClearAllPools();
        await using var connection = new NpgsqlConnection(TestDatabase.AdminConnectionString);
        await connection.OpenAsync();
        await using var drop = new NpgsqlCommand($"drop database if exists {_databaseName} with (force)", connection);
        await drop.ExecuteNonQueryAsync();
    }

    /// <summary>The real Database/schema.sql. If the server has no pgvector (a plain local install), the Knowledge
    /// Base's vector column becomes real[] — nothing the billing tests touch.</summary>
    private static async Task<string> LoadSchemaAsync(NpgsqlConnection db)
    {
        var path = FindSchemaFile();
        var script = await File.ReadAllTextAsync(path);
        await using var check = new NpgsqlCommand("select count(*) from pg_available_extensions where name = 'vector'", db);
        var hasVector = (long)(await check.ExecuteScalarAsync() ?? 0L) > 0;
        if (!hasVector)
        {
            script = script.Replace("create extension if not exists vector;", string.Empty)
                .Replace("vector(1536)", "real[]");
        }
        return script;
    }

    private static string FindSchemaFile()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "Database", "schema.sql");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("Database/schema.sql not found above the test output folder.");
    }
}

[CollectionDefinition("Postgres")]
public sealed class PostgresCollection : ICollectionFixture<PostgresFixture>
{
}
