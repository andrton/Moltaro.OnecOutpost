using Npgsql;

namespace Moltaro.OnecOutpost.TestSupport.Store;

// What a schema of PostgreSQL holds, as lines of text that compare across schemas: its tables with their columns,
// constraints and indexes, and the rows of every table, the record of applied migrations among them. The name of the
// schema is replaced by {schema} in every line, so the tests give their schemas names that occur nowhere else.
internal sealed class SchemaSnapshot
{
    private SchemaSnapshot(IReadOnlyList<string> tables, IReadOnlyList<string> lines)
    {
        Tables = tables;
        Lines = lines;
    }

    public IReadOnlyList<string> Tables { get; }

    public IReadOnlyList<string> Lines { get; }

    public static async Task<bool> ExistsAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        List<string> found = await QueryAsync(
            connection,
            "SELECT nspname FROM pg_namespace WHERE nspname = @schema",
            schema,
            cancellationToken);

        return found.Count > 0;
    }

    public static async Task<SchemaSnapshot> TakeAsync(string connectionString, string schema, CancellationToken cancellationToken)
    {
        await using NpgsqlConnection connection = new(connectionString);
        await connection.OpenAsync(cancellationToken);

        List<string> tables = await QueryAsync(
            connection,
            """
            SELECT table_name FROM information_schema.tables
            WHERE table_schema = @schema AND table_type = 'BASE TABLE'
            ORDER BY table_name
            """,
            schema,
            cancellationToken);

        List<string> lines = [.. tables.Select(table => $"table {table}")];

        lines.AddRange(await QueryAsync(
            connection,
            """
            SELECT 'column ' || table_name || ' ' || column_name || ' ' || udt_name || ' ' || is_nullable
                || ' ' || coalesce(column_default, '-') || ' ' || is_identity || ' ' || coalesce(identity_generation, '-')
            FROM information_schema.columns
            WHERE table_schema = @schema
            ORDER BY table_name, ordinal_position
            """,
            schema,
            cancellationToken));

        lines.AddRange(await QueryAsync(
            connection,
            """
            SELECT 'constraint ' || c.conrelid::regclass::text || ' ' || c.conname || ' ' || pg_get_constraintdef(c.oid)
            FROM pg_constraint c JOIN pg_namespace n ON n.oid = c.connamespace
            WHERE n.nspname = @schema
            ORDER BY 1
            """,
            schema,
            cancellationToken));

        lines.AddRange(await QueryAsync(
            connection,
            """
            SELECT 'index ' || tablename || ' ' || indexname || ' ' || indexdef
            FROM pg_indexes
            WHERE schemaname = @schema
            ORDER BY tablename, indexname
            """,
            schema,
            cancellationToken));

        foreach (string table in tables)
        {
            string name = $"{Quote(schema)}.{Quote(table)}";

            lines.AddRange(await QueryAsync(
                connection,
                $"SELECT 'row {table} ' || row_to_json(t)::text FROM {name} t ORDER BY 1",
                schema,
                cancellationToken));
        }

        string[] normalized = [.. lines.Select(line => line.Replace(schema, "{schema}", StringComparison.Ordinal))];

        return new SchemaSnapshot(tables, normalized);
    }

    private static async Task<List<string>> QueryAsync(
        NpgsqlConnection connection,
        string sql,
        string schema,
        CancellationToken cancellationToken)
    {
        await using NpgsqlCommand command = new(sql, connection);
        command.Parameters.AddWithValue("schema", schema);

        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken);

        List<string> values = [];

        while (await reader.ReadAsync(cancellationToken))
        {
            values.Add(reader.GetString(0));
        }

        return values;
    }

    private static string Quote(string identifier)
    {
        return "\"" + identifier.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
    }
}
