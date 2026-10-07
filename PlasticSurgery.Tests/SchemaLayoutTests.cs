using Microsoft.EntityFrameworkCore;
using Npgsql;
using PlasticSurgery.Persistence.Contexts;

namespace PlasticSurgery.Tests;

/// <summary>Database/schema.sql and the EF mapping agree on where every table lives (one schema per area, nothing in public).</summary>
[Collection("Postgres")]
public class SchemaLayoutTests
{
    private readonly PostgresFixture _db;

    public SchemaLayoutTests(PostgresFixture db) => _db = db;

    [PostgresFact]
    public async Task No_table_is_left_in_public()
    {
        await using var connection = new NpgsqlConnection(_db.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("select string_agg(tablename, ', ') from pg_tables where schemaname = 'public'", connection);
        Assert.Null(await command.ExecuteScalarAsync() as string);
    }

    [PostgresFact]
    public async Task Every_mapped_entity_has_its_table_in_the_mapped_schema()
    {
        await using var context = new ApplicationDbContext(new DbContextOptionsBuilder<ApplicationDbContext>().UseNpgsql(_db.ConnectionString).Options);
        await using var connection = new NpgsqlConnection(_db.ConnectionString);
        await connection.OpenAsync();

        var missing = new List<string>();
        foreach (var entity in context.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null) continue;
            var schema = entity.GetSchema();
            Assert.False(string.IsNullOrEmpty(schema), $"{entity.ClrType.Name} ({table}) has no schema in ApplicationDbContext.");
            await using var command = new NpgsqlCommand("select to_regclass(@name)::text", connection);
            command.Parameters.AddWithValue("name", $"{schema}.{table}");
            if (await command.ExecuteScalarAsync() is not string) missing.Add($"{schema}.{table}");
        }

        Assert.Empty(missing);
    }
}
