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
    /// <summary>
    /// Applies the migrations of the store that the schema lacks, and creates the schema when it does not exist.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection. Not <see langword="null"/>, empty or white space.</param>
    /// <param name="schema">The schema of the store: 1 to 63 characters, each one of <c>a–z 0–9 _</c>, the first a letter.</param>
    /// <param name="cancellationToken">Cancels the call.</param>
    /// <returns>A task that completes when the schema has every migration.</returns>
    /// <remarks>
    /// A schema that has every migration is left as it is, so a second call changes nothing.
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
    public static Task MigrateAsync(string connectionString, string schema, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ServerNames.CheckSchema(schema, nameof(schema));

        throw new NotSupportedException();
    }

    /// <summary>
    /// Gives one SQL script per migration of the store, in order, for an application that applies migrations itself.
    /// </summary>
    /// <param name="schema">The schema the scripts work in: 1 to 63 characters, each one of <c>a–z 0–9 _</c>, the first a letter.</param>
    /// <returns>The scripts, oldest first.</returns>
    /// <remarks>
    /// Applying them in order to an empty schema gives the same tables, and the same record of applied migrations, as
    /// <see cref="MigrateAsync"/> does. The scripts come from the library itself; nothing connects to a database.
    /// </remarks>
    /// <exception cref="ArgumentNullException">Thrown by the call: <paramref name="schema"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">
    /// Thrown by the call: <paramref name="schema"/> breaks the rule of schemas.
    /// </exception>
    public static IReadOnlyList<OutpostMigrationScript> GetScripts(string schema)
    {
        ServerNames.CheckSchema(schema, nameof(schema));

        throw new NotSupportedException();
    }
}
