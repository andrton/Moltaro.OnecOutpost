using Microsoft.EntityFrameworkCore;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.Server.Tests.Migrations;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// A schema of one test, migrated by the library, and the rows that the store committed in it. Each read opens a context
// of its own, as each operation of the store does. The tests of the endpoints read the store of their node through it.
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

    // The bases of the agent's newest poll, in ordinal order of their keys, each with whether it opens Write.
    public async Task<IReadOnlyList<(string Key, bool WriteOpen)>> FindBasesAsync(
        Guid agentId,
        CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(ConnectionString, Name);

        List<AgentBaseRow> bases = await context.AgentBases
            .Where(agentBase => agentBase.AgentId == agentId)
            .ToListAsync(cancellationToken);

        IEnumerable<AgentBaseRow> ordered = bases.OrderBy(agentBase => agentBase.Key, StringComparer.Ordinal);

        return [.. ordered.Select(agentBase => (agentBase.Key, agentBase.WriteOpen))];
    }

    // The agent that holds the key, or null when no claim names it.
    public async Task<Guid?> FindHolderAsync(string key, CancellationToken cancellationToken)
    {
        await using StoreContext context = StoreContextFactory.Create(ConnectionString, Name);

        BaseClaimRow? claim = await context.BaseClaims.SingleOrDefaultAsync(row => row.Key == key, cancellationToken);

        return claim?.AgentId;
    }

    // Every row of every table of the schema, as SchemaSnapshot gives them: a test that states that a request or an
    // operation changes nothing compares the lines of two of them.
    public Task<SchemaSnapshot> TakeSnapshotAsync(CancellationToken cancellationToken)
    {
        return SchemaSnapshot.TakeAsync(ConnectionString, Name, cancellationToken);
    }
}
