using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// The migrations of the store: applied by the library, or given as SQL to an application that applies migrations
/// itself.
/// </summary>
/// <remarks>
/// The store is an EF Core context of the library on PostgreSQL, in a schema the application chooses, and its
/// migrations are EF Core migrations. An application runs one of the two ways before the server serves, with the
/// connection and the schema of <see cref="OutpostServerOptions"/>.
/// </remarks>
public static class OutpostMigrations
{
    // The first half of the key of the advisory lock under which the migrations of one schema run; the second half is
    // the schema. Nodes that migrate one schema must agree on it.
    private const string MigrationLockClass = "Moltaro.OnecOutpost.Migrations";

    /// <summary>
    /// Applies the migrations of the store that the schema lacks, and creates the schema when it does not exist.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection. Not <see langword="null"/>, empty or white space.</param>
    /// <param name="schema">The schema of the store: 1 to 63 characters, each one of <c>a–z 0–9 _</c>, the first a letter.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the schema has every migration.</returns>
    /// <remarks>
    /// A schema that has every migration is left as it is, so a second call changes nothing. Calls for one schema at the
    /// same time, on one node or on several, run one after another. When the database of the connection does not exist
    /// and the role of the connection may create one, the call creates the database first.
    /// </remarks>
    /// <exception cref="ArgumentNullException">
    /// Thrown by the call: <paramref name="connectionString"/> or <paramref name="schema"/> is <see langword="null"/>.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="connectionString"/> is empty or white space, or <paramref name="schema"/>
    /// breaks the rule of schemas.
    /// </exception>
    /// <exception cref="OperationCanceledException">
    /// Reported by the task: <paramref name="cancellationToken"/> was cancelled.
    /// </exception>
    /// <exception cref="Npgsql.NpgsqlException">
    /// Reported by the task: the database failed, for example it refused the connection or the role lacks a right; a
    /// <see cref="Npgsql.PostgresException"/> when the server reported the error.
    /// </exception>
    public static Task MigrateAsync(string connectionString, string schema, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ServerNames.CheckSchema(schema, nameof(schema));

        return MigrateSchemaAsync(connectionString, schema, cancellationToken);
    }

    /// <summary>
    /// Gives one SQL script per migration of the store, in order, for an application that applies migrations itself.
    /// </summary>
    /// <param name="schema">The schema the scripts work in: 1 to 63 characters, each one of <c>a–z 0–9 _</c>, the first a letter.</param>
    /// <returns>The scripts, oldest first.</returns>
    /// <remarks>
    /// Applying them in order to an empty schema gives the same tables, and the same record of applied migrations, as
    /// <see cref="MigrateAsync"/> does. The scripts come from the library itself; nothing connects to a database. Each
    /// script carries its own transaction around its migration, <c>START TRANSACTION</c> … <c>COMMIT</c>, so a tool that
    /// applies it should not wrap it in another.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown by the call: <paramref name="schema"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="schema"/> breaks the rule of schemas.
    /// </exception>
    public static IReadOnlyList<OutpostMigrationScript> GetScripts(string schema)
    {
        ServerNames.CheckSchema(schema, nameof(schema));

        using StoreContext context = StoreContextFactory.Create(null, schema);
        IMigrator migrator = context.GetService<IMigrator>();

        List<OutpostMigrationScript> scripts = [];
        string? previous = null;

        // Each script runs from the migration before it, so it holds its own migration only; the first, from nothing,
        // also creates the schema and the record of applied migrations, as MigrateAsync does.
        foreach (string migration in context.Database.GetMigrations())
        {
            scripts.Add(new OutpostMigrationScript(migration, migrator.GenerateScript(previous, migration)));
            previous = migration;
        }

        return scripts;
    }

    // Apart from MigrateAsync, so that its argument checks throw from the call itself, not from the task.
    private static async Task MigrateSchemaAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(connectionString, schema);

        // EF Core would create a missing database itself, but the lock below needs a connection to it first.
        IRelationalDatabaseCreator database = context.GetService<IRelationalDatabaseCreator>();

        if (!await database.ExistsAsync(cancellationToken))
        {
            await database.CreateAsync(cancellationToken);
        }

        // EF Core and Npgsql create the schema and its history table before they lock that table, and several migrations
        // of one new schema at once can collide there. A session advisory lock with the key
        // (hashtext('Moltaro.OnecOutpost.Migrations'), hashtext(schema)) makes the migrations of one schema run one after
        // another, on any node. It is held on the connection of the context, opened here so that the migration runs in
        // the session that holds it. The wait for it ends with the token or with the command timeout of the connection.
        await context.Database.OpenConnectionAsync(cancellationToken);

        await context.Database.ExecuteSqlAsync(
            $"SELECT pg_advisory_lock(hashtext({MigrationLockClass}), hashtext({schema}))",
            cancellationToken);

        try
        {
            await context.Database.MigrateAsync(cancellationToken);
        }
        finally
        {
            // Without the token, so that a cancelled call releases the lock too: on a connection that goes back to the
            // pool, the session and its lock would outlive the context.
            await context.Database.ExecuteSqlAsync(
                $"SELECT pg_advisory_unlock(hashtext({MigrationLockClass}), hashtext({schema}))",
                CancellationToken.None);
        }
    }
}
