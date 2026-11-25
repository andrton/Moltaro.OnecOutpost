using Moltaro.OnecOutpost.TestSupport;
using Npgsql;

namespace Moltaro.OnecOutpost.Shell.Tests.Sessions;

// The shell always uses the schema outpost. Each test gets a fresh database in the shared PostgreSQL container so
// it runs the shell's own migrations and cannot read another session's agents or key.
internal static class ShellDatabase
{
    public static async Task<string> CreateAsync(PostgreSqlFixture postgres, CancellationToken cancellationToken)
    {
        string main = await postgres.GetConnectionStringAsync(cancellationToken);
        string database = $"shell_{Guid.NewGuid():N}";
        await using NpgsqlConnection connection = new(main);
        await connection.OpenAsync(cancellationToken);
        await using NpgsqlCommand create = new($"CREATE DATABASE {database}", connection);
        await create.ExecuteNonQueryAsync(cancellationToken);

        return new NpgsqlConnectionStringBuilder(main) { Database = database }.ConnectionString;
    }
}
