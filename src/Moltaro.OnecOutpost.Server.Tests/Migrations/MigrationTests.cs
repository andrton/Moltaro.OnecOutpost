using Microsoft.EntityFrameworkCore;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Npgsql;

namespace Moltaro.OnecOutpost.Server.Tests.Migrations;

// The migrations of the store against a real PostgreSQL. Each test works in schemas of its own, with names that occur
// nowhere else, as SchemaSnapshot requires.
[Trait("Requires", "PostgreSQL")]
public sealed class MigrationTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
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

    [Fact]
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

    [Fact]
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

    // A token cancelled before the call gives a cancelled task, and the call changes nothing (the public contract,
    // "Rules for every member").
    [Fact]
    public async Task MigrateAsync_with_a_cancelled_token_gives_a_cancelled_task_and_creates_no_schema()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string Schema = "migration_cancelled";

        Task migration = OutpostMigrations.MigrateAsync(connectionString, Schema, new CancellationToken(canceled: true));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => migration);
        Assert.True(migration.IsCanceled);
        Assert.False(await SchemaSnapshot.ExistsAsync(connectionString, Schema, cancellationToken));
    }

    // The snapshot takes the schema, as the model does. A schema other than outpost, the default of the options, shows a
    // snapshot made for one schema only.
    [Fact]
    public async Task The_model_has_no_pending_changes_against_its_snapshot()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);

        await using StoreContext context = StoreContextFactory.Create(connectionString, "model_against_snapshot");

        Assert.False(context.Database.HasPendingModelChanges());
    }

    // Driven to the order in which both migrations start on a schema that is still new: another connection creates it
    // and holds it uncommitted until both are seen waiting, one to create it and the other behind it for the lock of
    // the schema, then rolls back, which leaves the schema new to both.
    [Fact]
    public async Task Two_MigrateAsync_running_at_once_into_one_new_schema_both_complete_and_give_the_schema_that_one_gives()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string AloneSchema = "migrated_alone";
        const string AtOnceSchema = "migrated_at_once";

        await OutpostMigrations.MigrateAsync(connectionString, AloneSchema, cancellationToken);

        await using HeldLock schemaLock = await HeldLock.TakeAsync(
            connectionString,
            $"CREATE SCHEMA {AtOnceSchema}",
            cancellationToken);

        Task first = OutpostMigrations.MigrateAsync(connectionString, AtOnceSchema, cancellationToken);
        Task second = OutpostMigrations.MigrateAsync(connectionString, AtOnceSchema, cancellationToken);

        await schemaLock.WaitForAsync([first, second], cancellationToken);
        await schemaLock.ReleaseAsync(cancellationToken);

        await Task.WhenAll(first, second);

        SchemaSnapshot alone = await SchemaSnapshot.TakeAsync(connectionString, AloneSchema, cancellationToken);
        SchemaSnapshot atOnce = await SchemaSnapshot.TakeAsync(connectionString, AtOnceSchema, cancellationToken);

        Assert.NotEmpty(alone.Tables);
        Assert.Equal(alone.Lines, atOnce.Lines);
    }

    // The migrations of one schema run one after another, under the session advisory lock with the key
    // (hashtext('Moltaro.OnecOutpost.Migrations'), hashtext(schema)): EF Core and Npgsql create the schema and its history
    // table before they lock that table, where four or more migrations at once can collide. Another connection holds the
    // lock until all four are seen waiting for it, then lets them go; without the lock none would wait.
    [Fact]
    public async Task Four_MigrateAsync_at_once_into_one_new_schema_wait_for_its_lock_and_all_give_the_schema_that_one_gives()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string AloneSchema = "four_compared_alone";
        const string AtOnceSchema = "four_at_once";

        await OutpostMigrations.MigrateAsync(connectionString, AloneSchema, cancellationToken);

        await using HeldLock migrationLock = await HeldLock.TakeAsync(
            connectionString,
            $"SELECT pg_advisory_xact_lock(hashtext('Moltaro.OnecOutpost.Migrations'), hashtext('{AtOnceSchema}'))",
            cancellationToken);

        Task[] migrations =
        [
            .. Enumerable.Range(0, 4).Select(_ => OutpostMigrations.MigrateAsync(connectionString, AtOnceSchema, cancellationToken)),
        ];

        await migrationLock.WaitForAsync(migrations, cancellationToken);
        await migrationLock.ReleaseAsync(cancellationToken);

        await Task.WhenAll(migrations);

        SchemaSnapshot alone = await SchemaSnapshot.TakeAsync(connectionString, AloneSchema, cancellationToken);
        SchemaSnapshot atOnce = await SchemaSnapshot.TakeAsync(connectionString, AtOnceSchema, cancellationToken);

        Assert.NotEmpty(alone.Tables);
        Assert.Equal(alone.Lines, atOnce.Lines);
    }

    // A wait for the lock of the schema honors cancellation (AGENTS.md, principle 10). Another connection holds the lock
    // until the migration is seen waiting for it, and the token is cancelled while it waits.
    [Fact]
    public async Task A_MigrateAsync_waiting_for_the_lock_of_its_schema_ends_cancelled_with_its_token_and_creates_no_schema()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string Schema = "migration_cancelled_waiting";

        await using HeldLock migrationLock = await HeldLock.TakeAsync(
            connectionString,
            $"SELECT pg_advisory_xact_lock(hashtext('Moltaro.OnecOutpost.Migrations'), hashtext('{Schema}'))",
            cancellationToken);

        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task migration = OutpostMigrations.MigrateAsync(connectionString, Schema, cancellation.Token);

        await migrationLock.WaitForAsync([migration], cancellationToken);
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => migration);

        await migrationLock.ReleaseAsync(cancellationToken);

        Assert.False(await SchemaSnapshot.ExistsAsync(connectionString, Schema, cancellationToken));
    }

    // The lock needs a connection to the database, so the database is created before it; the role of the container may
    // create one.
    [Fact]
    public async Task MigrateAsync_creates_a_database_that_does_not_exist()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string serverConnectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        string connectionString = new NpgsqlConnectionStringBuilder(serverConnectionString)
        {
            Database = "migration_into_new_database",
        }.ConnectionString;
        const string Schema = "schema_in_new_database";

        await OutpostMigrations.MigrateAsync(connectionString, Schema, cancellationToken);

        SchemaSnapshot migrated = await SchemaSnapshot.TakeAsync(connectionString, Schema, cancellationToken);
        Assert.Contains("agents", migrated.Tables);
    }

    // The record of applied migrations is in the schema as well: kept anywhere else, the second schema would find the
    // migrations of the first applied and get no tables.
    [Fact]
    public async Task Two_schemas_in_one_database_each_get_their_own_tables()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string FirstSchema = "own_tables_first";
        const string SecondSchema = "own_tables_second";

        await OutpostMigrations.MigrateAsync(connectionString, FirstSchema, cancellationToken);
        SchemaSnapshot firstAlone = await SchemaSnapshot.TakeAsync(connectionString, FirstSchema, cancellationToken);

        await OutpostMigrations.MigrateAsync(connectionString, SecondSchema, cancellationToken);
        SchemaSnapshot first = await SchemaSnapshot.TakeAsync(connectionString, FirstSchema, cancellationToken);
        SchemaSnapshot second = await SchemaSnapshot.TakeAsync(connectionString, SecondSchema, cancellationToken);

        Assert.Contains("agents", second.Tables);
        Assert.Contains("pairing_tokens", second.Tables);
        Assert.Equal(first.Lines, second.Lines);
        Assert.Equal(firstAlone.Lines, first.Lines);
    }

    // A model built for one schema and reused for another would show here: the context of the other schema would read
    // the row written in the first, or a context would work in a schema that this test did not migrate.
    [Fact]
    public async Task A_context_of_one_schema_never_reads_the_rows_of_another()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);
        const string WrittenSchema = "rows_written";
        const string OtherSchema = "rows_not_written";
        Guid agentId = Guid.NewGuid();

        await OutpostMigrations.MigrateAsync(connectionString, WrittenSchema, cancellationToken);
        await OutpostMigrations.MigrateAsync(connectionString, OtherSchema, cancellationToken);

        await using (StoreContext written = StoreContextFactory.Create(connectionString, WrittenSchema))
        {
            written.Agents.Add(new AgentRow
            {
                Id = agentId,
                Name = "Agent of one schema",
                State = OutpostAgentStateEnum.Pending,
                CreatedAt = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero),
            });

            await written.SaveChangesAsync(cancellationToken);
        }

        await using (StoreContext other = StoreContextFactory.Create(connectionString, OtherSchema))
        {
            Assert.Empty(await other.Agents.ToListAsync(cancellationToken));
        }

        await using (StoreContext written = StoreContextFactory.Create(connectionString, WrittenSchema))
        {
            AgentRow agent = Assert.Single(await written.Agents.ToListAsync(cancellationToken));
            Assert.Equal(agentId, agent.Id);
        }
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql, CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }
}
