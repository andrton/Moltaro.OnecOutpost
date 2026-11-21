using Microsoft.EntityFrameworkCore;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// A schema of one test, migrated by the library, and the rows that the store committed in it. Each read opens a context
// of its own, as each operation of the store does.
internal sealed class MigratedSchema
{
    private MigratedSchema(string connectionString, string name)
    {
        ConnectionString = connectionString;
        Name = name;
    }

    public string ConnectionString { get; }

    public string Name { get; }

    public static async Task<MigratedSchema> CreateAsync(
        PostgreSqlFixture postgres,
        string name,
        CancellationToken cancellationToken)
    {
        string connectionString = await postgres.GetConnectionStringAsync(cancellationToken);

        await OutpostMigrations.MigrateAsync(connectionString, name, cancellationToken);

        return new MigratedSchema(connectionString, name);
    }

    public async Task<AgentRow?> FindAgentAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(ConnectionString, Name);

        return await context.Agents.SingleOrDefaultAsync(agent => agent.Id == agentId, cancellationToken);
    }

    public async Task<PairingTokenRow?> FindTokenAsync(Guid agentId, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(ConnectionString, Name);

        return await context.PairingTokens.SingleOrDefaultAsync(token => token.AgentId == agentId, cancellationToken);
    }
}
