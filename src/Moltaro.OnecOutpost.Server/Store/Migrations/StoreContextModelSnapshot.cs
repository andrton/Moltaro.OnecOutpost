using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace Moltaro.OnecOutpost.Server.Store.Migrations;

// The model of the store as its newest migration leaves it, against which EF Core checks the model for changes that no
// migration has. Scaffolded by the EF Core tools for one schema; the schema was then made a parameter by hand, as the
// model of the store takes it, so that the check compares like with like in every schema.
internal sealed class StoreContextModelSnapshot : ModelSnapshot
{
    private readonly string _schema;

    public StoreContextModelSnapshot(string schema)
    {
        _schema = schema;
    }

    protected override void BuildModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema(_schema)
            .HasAnnotation("ProductVersion", "10.0.15")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.AgentBaseRow", agentBase =>
        {
            agentBase.Property<Guid>("AgentId")
                .HasColumnType("uuid")
                .HasColumnName("agent_id");

            agentBase.Property<string>("Key")
                .HasColumnType("text")
                .HasColumnName("key");

            agentBase.Property<bool>("WriteOpen")
                .HasColumnType("boolean")
                .HasColumnName("write_open");

            agentBase.HasKey("AgentId", "Key");

            agentBase.ToTable("agent_bases", _schema);
        });

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.AgentRow", agent =>
        {
            agent.Property<Guid>("Id")
                .HasColumnType("uuid")
                .HasColumnName("id");

            agent.Property<string>("AgentVersion")
                .HasColumnType("text")
                .HasColumnName("agent_version");

            agent.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");

            agent.Property<DateTimeOffset?>("LastPollAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("last_poll_at");

            agent.Property<DateTimeOffset?>("LeaseEndsAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("lease_ends_at");

            agent.Property<string>("Name")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("name");

            agent.Property<long?>("NewestPoll")
                .HasColumnType("bigint")
                .HasColumnName("newest_poll");

            agent.Property<DateTimeOffset?>("PairedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("paired_at");

            agent.Property<byte[]>("PublicKey")
                .HasColumnType("bytea")
                .HasColumnName("public_key");

            agent.Property<int>("State")
                .HasColumnType("integer")
                .HasColumnName("state");

            agent.HasKey("Id");

            agent.ToTable("agents", _schema);
        });

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.BaseClaimRow", claim =>
        {
            claim.Property<string>("Key")
                .HasColumnType("text")
                .HasColumnName("key");

            claim.Property<Guid>("AgentId")
                .HasColumnType("uuid")
                .HasColumnName("agent_id");

            claim.HasKey("Key");

            claim.HasIndex("AgentId");

            claim.ToTable("base_claims", _schema);
        });

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.PairingTokenRow", token =>
        {
            token.Property<Guid>("AgentId")
                .HasColumnType("uuid")
                .HasColumnName("agent_id");

            token.Property<DateTimeOffset>("ExpiresAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("expires_at");

            token.Property<byte[]>("Secret")
                .IsRequired()
                .HasColumnType("bytea")
                .HasColumnName("secret");

            token.HasKey("AgentId");

            token.ToTable("pairing_tokens", _schema);
        });

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.AgentBaseRow", agentBase =>
        {
            agentBase.HasOne("Moltaro.OnecOutpost.Server.Store.AgentRow", null)
                .WithMany()
                .HasForeignKey("AgentId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.BaseClaimRow", claim =>
        {
            claim.HasOne("Moltaro.OnecOutpost.Server.Store.AgentRow", null)
                .WithMany()
                .HasForeignKey("AgentId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.PairingTokenRow", token =>
        {
            token.HasOne("Moltaro.OnecOutpost.Server.Store.AgentRow", null)
                .WithOne()
                .HasForeignKey("Moltaro.OnecOutpost.Server.Store.PairingTokenRow", "AgentId")
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
    }
}
