using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Moltaro.OnecOutpost.Server.Store.Migrations;

// The second migration of the store: what a poll records on its agent, the bases of the agent's newest poll and the
// claims of base keys. Written in the form the EF Core tools scaffold for one schema, with the schema a parameter, as in
// the first migration, so that it serves the schema it is created with. Its target model is the model of the store as
// this migration leaves it.
[Migration(Id)]
internal sealed class PollsAndBaseClaims : Migration
{
    public const string Id = "20261121224645_PollsAndBaseClaims";

    private readonly string _schema;

    public PollsAndBaseClaims(string schema)
    {
        _schema = schema;
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<string>(
            name: "agent_version",
            schema: _schema,
            table: "agents",
            type: "text",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "last_poll_at",
            schema: _schema,
            table: "agents",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<DateTimeOffset>(
            name: "lease_ends_at",
            schema: _schema,
            table: "agents",
            type: "timestamp with time zone",
            nullable: true);

        migrationBuilder.AddColumn<long>(
            name: "newest_poll",
            schema: _schema,
            table: "agents",
            type: "bigint",
            nullable: true);

        migrationBuilder.CreateTable(
            name: "agent_bases",
            schema: _schema,
            columns: table => new
            {
                agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                key = table.Column<string>(type: "text", nullable: false),
                write_open = table.Column<bool>(type: "boolean", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agent_bases", columns => new { columns.agent_id, columns.key });
                table.ForeignKey(
                    name: "FK_agent_bases_agents_agent_id",
                    column: columns => columns.agent_id,
                    principalSchema: _schema,
                    principalTable: "agents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateTable(
            name: "base_claims",
            schema: _schema,
            columns: table => new
            {
                key = table.Column<string>(type: "text", nullable: false),
                agent_id = table.Column<Guid>(type: "uuid", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_base_claims", columns => columns.key);
                table.ForeignKey(
                    name: "FK_base_claims_agents_agent_id",
                    column: columns => columns.agent_id,
                    principalSchema: _schema,
                    principalTable: "agents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_base_claims_agent_id",
            schema: _schema,
            table: "base_claims",
            column: "agent_id");
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
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
