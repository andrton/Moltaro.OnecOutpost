namespace Moltaro.OnecOutpost.Server;

/// <summary>
/// The SQL of one migration of the store, for an application that applies migrations itself: one entry of
/// <see cref="OutpostMigrations.GetScripts"/>.
/// </summary>
/// <param name="Name">The identifier of the EF Core migration.</param>
/// <param name="Sql">The SQL that applies it to the schema given to <see cref="OutpostMigrations.GetScripts"/>.</param>
public sealed record OutpostMigrationScript(string Name, string Sql);
