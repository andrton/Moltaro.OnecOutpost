namespace Moltaro.OnecOutpost.Server.Store;

// The one place that builds the options of a StoreContext: Npgsql, the schema as the default schema of the model and of
// the history table of the migrations, a model cache key that includes the schema, and the library's own migrations
// assembly. Without a connection string the context serves GetScripts, which opens no connection.
internal static class StoreContextFactory
{
    public static StoreContext Create(string? connectionString, string schema)
    {
        throw new NotSupportedException();
    }
}
