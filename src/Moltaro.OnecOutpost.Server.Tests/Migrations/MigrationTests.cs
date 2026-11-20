using Moltaro.OnecOutpost.TestSupport;
using Npgsql;

namespace Moltaro.OnecOutpost.Server.Tests.Migrations;

// The migrations of the store against a real PostgreSQL. Each test works in schemas of its own, with names that occur
// nowhere else, as SchemaSnapshot requires.
[Trait("Requires", "PostgreSQL")]
public sealed class MigrationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task MigrateAsync_creates_a_schema_that_does_not_exist_with_the_tables_of_the_store()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string Schema = "migration_into_new_schema";

        Assert.False(await SchemaSnapshot.ExistsAsync(connectionString, Schema, cancellationToken));

        await OutpostMigrations.MigrateAsync(connectionString, Schema, cancellationToken);

        Assert.True(await SchemaSnapshot.ExistsAsync(connectionString, Schema, cancellationToken));

        SchemaSnapshot migrated = await SchemaSnapshot.TakeAsync(connectionString, Schema, cancellationToken);
        Assert.NotEmpty(migrated.Tables);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_second_MigrateAsync_changes_nothing()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string Schema = "migration_repeated";

        await OutpostMigrations.MigrateAsync(connectionString, Schema, cancellationToken);
        SchemaSnapshot first = await SchemaSnapshot.TakeAsync(connectionString, Schema, cancellationToken);

        await OutpostMigrations.MigrateAsync(connectionString, Schema, cancellationToken);
        SchemaSnapshot second = await SchemaSnapshot.TakeAsync(connectionString, Schema, cancellationToken);

        Assert.Equal(first.Lines, second.Lines);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_scripts_applied_in_order_to_an_empty_schema_give_the_tables_and_the_record_that_MigrateAsync_gives()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string MigratedSchema = "migration_by_the_library";
        const string ScriptedSchema = "migration_by_the_scripts";

        await OutpostMigrations.MigrateAsync(connectionString, MigratedSchema, cancellationToken);

        await using (NpgsqlConnection connection = new(connectionString))
        {
            await connection.OpenAsync(cancellationToken);
            await ExecuteAsync(connection, $"CREATE SCHEMA {ScriptedSchema}", cancellationToken);

            foreach (OutpostMigrationScript script in OutpostMigrations.GetScripts(ScriptedSchema))
            {
                await ExecuteAsync(connection, script.Sql, cancellationToken);
            }
        }

        SchemaSnapshot migrated = await SchemaSnapshot.TakeAsync(connectionString, MigratedSchema, cancellationToken);
        SchemaSnapshot scripted = await SchemaSnapshot.TakeAsync(connectionString, ScriptedSchema, cancellationToken);

        Assert.NotEmpty(migrated.Tables);
        Assert.Equal(migrated.Lines, scripted.Lines);
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
