using Microsoft.EntityFrameworkCore;

namespace Moltaro.OnecOutpost.Server.Store;

// The EF Core context of the store, in the schema the application chose. StoreContextFactory builds its options; the
// model takes the schema from the context, so one model serves every schema.
internal sealed class StoreContext : DbContext
{
    public StoreContext(DbContextOptions<StoreContext> options, string schema)
        : base(options)
    {
        Schema = schema;
    }

    public string Schema { get; }

    public DbSet<AgentRow> Agents => Set<AgentRow>();

    public DbSet<PairingTokenRow> PairingTokens => Set<PairingTokenRow>();

    // Every table and column is named here, in snake case. Every time is a timestamptz.
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema(Schema);

        modelBuilder.Entity<AgentRow>(agent =>
        {
            agent.ToTable("agents");

            agent.HasKey(row => row.Id);

            // The identity comes from the caller, which puts it into the pairing token.
            agent.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            agent.Property(row => row.Name).HasColumnName("name");
            agent.Property(row => row.State).HasColumnName("state");
            agent.Property(row => row.CreatedAt).HasColumnName("created_at").HasColumnType("timestamp with time zone");
            agent.Property(row => row.PairedAt).HasColumnName("paired_at").HasColumnType("timestamp with time zone");
            agent.Property(row => row.PublicKey).HasColumnName("public_key");
        });

        modelBuilder.Entity<PairingTokenRow>(token =>
        {
            token.ToTable("pairing_tokens");

            token.HasKey(row => row.AgentId);

            token.Property(row => row.AgentId).HasColumnName("agent_id");
            token.Property(row => row.Secret).HasColumnName("secret");
            token.Property(row => row.ExpiresAt).HasColumnName("expires_at").HasColumnType("timestamp with time zone");

            // A token belongs to one agent and is deleted with it.
            token.HasOne<AgentRow>()
                .WithOne()
                .HasForeignKey<PairingTokenRow>(row => row.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
