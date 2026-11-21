using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Infrastructure;

namespace Moltaro.OnecOutpost.Server.Store;

// The one place that builds the options of a StoreContext: Npgsql, the schema as the default schema of the model and of
// the history table of the migrations, a model cache key that includes the schema, and the library's own migrations
// assembly. Without a connection string the context serves GetScripts, which opens no connection. The check of pending
// model changes stays on, and nothing retries.
internal static class StoreContextFactory
{
    public static StoreContext Create(string? connectionString, string schema)
    {
        // Kept in the schema, so that each schema records its own migrations.
        void ConfigureNpgsql(NpgsqlDbContextOptionsBuilder npgsql)
        {
            npgsql.MigrationsHistoryTable(HistoryRepository.DefaultTableName, schema);
        }

        DbContextOptionsBuilder<StoreContext> options = new();

        if (connectionString is null)
        {
            options.UseNpgsql(ConfigureNpgsql);
        }
        else
        {
            options.UseNpgsql(connectionString, ConfigureNpgsql);
        }

        options.ReplaceService<IModelCacheKeyFactory, StoreModelCacheKeyFactory>();
        options.ReplaceService<IMigrationsAssembly, StoreMigrationsAssembly>();

        return new StoreContext(options.Options, schema);
    }
}
