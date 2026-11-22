using Microsoft.EntityFrameworkCore;

namespace Moltaro.OnecOutpost.Server.Store;

// The EF Core context of the store, in the schema the application chose. StoreContextFactory builds its options; the
// model takes the schema from the context, so one model serves every schema.
internal sealed class StoreContext : DbContext
{
    // The filter of the unique index of base and caller key, with the integers of the kinds Refused and Expired.
    private static readonly string CommandsHoldingTheirKey =
        $"caller_key IS NOT NULL AND (outcome_kind IS NULL OR outcome_kind NOT IN ({(int)OutpostOutcomeKindEnum.Refused}, {(int)OutpostOutcomeKindEnum.Expired}))";

    public StoreContext(DbContextOptions<StoreContext> options, string schema)
        : base(options)
    {
        Schema = schema;
    }

    public string Schema { get; }

    public DbSet<AgentRow> Agents => Set<AgentRow>();

    public DbSet<PairingTokenRow> PairingTokens => Set<PairingTokenRow>();

    public DbSet<AgentBaseRow> AgentBases => Set<AgentBaseRow>();

    public DbSet<BaseClaimRow> BaseClaims => Set<BaseClaimRow>();

    public DbSet<CommandRow> Commands => Set<CommandRow>();

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
            agent.Property(row => row.NewestPoll).HasColumnName("newest_poll");
            agent.Property(row => row.LastPollAt).HasColumnName("last_poll_at").HasColumnType("timestamp with time zone");
            agent.Property(row => row.LeaseEndsAt).HasColumnName("lease_ends_at").HasColumnType("timestamp with time zone");
            agent.Property(row => row.AgentVersion).HasColumnName("agent_version");
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

        modelBuilder.Entity<AgentBaseRow>(agentBase =>
        {
            agentBase.ToTable("agent_bases");

            agentBase.HasKey(row => new { row.AgentId, row.Key });

            agentBase.Property(row => row.AgentId).HasColumnName("agent_id");
            agentBase.Property(row => row.Key).HasColumnName("key");
            agentBase.Property(row => row.WriteOpen).HasColumnName("write_open");

            // The bases of an agent are deleted with it.
            agentBase.HasOne<AgentRow>()
                .WithMany()
                .HasForeignKey(row => row.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<BaseClaimRow>(claim =>
        {
            claim.ToTable("base_claims");

            // One claim per key, which settles the race of two claims of one key in one statement (rule 14).
            claim.HasKey(row => row.Key);

            claim.Property(row => row.Key).HasColumnName("key");
            claim.Property(row => row.AgentId).HasColumnName("agent_id");

            // The claims of one agent are found by the agent, for its poll answers and its revocation.
            claim.HasIndex(row => row.AgentId);

            // The claims of an agent are deleted with it.
            claim.HasOne<AgentRow>()
                .WithMany()
                .HasForeignKey(row => row.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<CommandRow>(command =>
        {
            command.ToTable("commands");

            command.HasKey(row => row.Id);

            // The identity comes from the server, which signs it into the bytes before the row is written.
            command.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
            command.Property(row => row.Sequence).HasColumnName("sequence").UseIdentityByDefaultColumn();
            command.Property(row => row.AgentId).HasColumnName("agent_id");
            command.Property(row => row.BaseKey).HasColumnName("base_key");
            command.Property(row => row.Mode).HasColumnName("mode");
            command.Property(row => row.CallerKey).HasColumnName("caller_key");
            command.Property(row => row.Bytes).HasColumnName("bytes");
            command.Property(row => row.Signature).HasColumnName("signature");
            command.Property(row => row.Deadline).HasColumnName("deadline").HasColumnType("timestamp with time zone");
            command.Property(row => row.Skew).HasColumnName("skew").HasColumnType("interval");
            command.Property(row => row.OutcomeLimit).HasColumnName("outcome_limit");
            command.Property(row => row.Phase).HasColumnName("phase");
            command.Property(row => row.RunLimit).HasColumnName("run_limit").HasColumnType("interval");
            command.Property(row => row.OutcomeKind).HasColumnName("outcome_kind");
            command.Property(row => row.OutcomeCode).HasColumnName("outcome_code");
            command.Property(row => row.Outcome).HasColumnName("outcome");
            command.Property(row => row.EndedAt).HasColumnName("ended_at").HasColumnType("timestamp with time zone");

            // One command per base and caller key among those that hold their key: every command but one that ended
            // as Expired or Refused, which frees it (rule 4).
            command.HasIndex(row => new { row.BaseKey, row.CallerKey })
                .IsUnique()
                .HasFilter(CommandsHoldingTheirKey);

            // The commands of an agent without an outcome, oldest first, for the take.
            command.HasIndex(row => new { row.AgentId, row.Sequence })
                .HasFilter("outcome_kind IS NULL");

            // The ended commands by the moment they ended, for the purge.
            command.HasIndex(row => row.EndedAt);

            // The commands of an agent are deleted with it.
            command.HasOne<AgentRow>()
                .WithMany()
                .HasForeignKey(row => row.AgentId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
