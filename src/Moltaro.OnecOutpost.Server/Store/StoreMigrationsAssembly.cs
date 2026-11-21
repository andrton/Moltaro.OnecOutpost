using System.Reflection;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Moltaro.OnecOutpost.Server.Store.Migrations;

namespace Moltaro.OnecOutpost.Server.Store;

// The migrations of the store and its snapshot, for the schema of the context that uses them. EF Core's own migrations
// assembly finds them by reflection and creates them without arguments, so it could give them no schema; this one lists
// them by hand and creates each with the schema. EF Core creates one for every context, so the schema is the context's.
//
// A new migration is scaffolded by the EF Core tools against a copy of the model with one schema, and its schema literal
// is then replaced by the parameter, as in the first migration; it is added to Migrations and to CreateMigration. The
// snapshot the tools wrote replaces StoreContextModelSnapshot, given the schema the same way.
internal sealed class StoreMigrationsAssembly : IMigrationsAssembly
{
    // By identifier, which begins with the time the migration was scaffolded, so the oldest comes first.
    private static readonly SortedList<string, TypeInfo> All = new(StringComparer.Ordinal)
    {
        [AgentsAndPairingTokens.Id] = typeof(AgentsAndPairingTokens).GetTypeInfo(),
    };

    private readonly string _schema;

    public StoreMigrationsAssembly(ICurrentDbContext currentContext)
    {
        _schema = ((StoreContext)currentContext.Context).Schema;
        ModelSnapshot = new StoreContextModelSnapshot(_schema);
    }

    public IReadOnlyDictionary<string, TypeInfo> Migrations => All;

    public ModelSnapshot ModelSnapshot { get; }

    public Assembly Assembly => typeof(StoreMigrationsAssembly).Assembly;

    // The library names a migration only by its identifier, never by the name alone.
    public string? FindMigrationId(string nameOrId)
    {
        return All.ContainsKey(nameOrId) ? nameOrId : null;
    }

    public Migration CreateMigration(TypeInfo migrationClass, string activeProvider)
    {
        if (migrationClass.AsType() != typeof(AgentsAndPairingTokens))
        {
            throw new ArgumentException("The type is not a migration of the store.", nameof(migrationClass));
        }

        return new AgentsAndPairingTokens(_schema) { ActiveProvider = activeProvider };
    }
}
