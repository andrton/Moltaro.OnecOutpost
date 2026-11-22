using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

namespace Moltaro.OnecOutpost.Server.Store.Migrations;

// The third migration of the store: the commands, with the index that holds one command per base and caller key, the
// index of the take and that of the purge. Written in the form the EF Core tools scaffold for one schema, with the
// schema a parameter, as in the first migration, so that it serves the schema it is created with. Its target model is
// the model of the store as this migration leaves it.
[Migration(Id)]
internal sealed class Commands : Migration
{
    public const string Id = "20261122075013_Commands";

    private readonly string _schema;

    public Commands(string schema)
    {
        _schema = schema;
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "commands",
            schema: _schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                sequence = table.Column<long>(type: "bigint", nullable: false)
                    .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                base_key = table.Column<string>(type: "text", nullable: false),
                mode = table.Column<int>(type: "integer", nullable: false),
                caller_key = table.Column<string>(type: "text", nullable: true),
                bytes = table.Column<byte[]>(type: "bytea", nullable: false),
                signature = table.Column<byte[]>(type: "bytea", nullable: false),
                deadline = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                skew = table.Column<TimeSpan>(type: "interval", nullable: false),
                outcome_limit = table.Column<int>(type: "integer", nullable: false),
                phase = table.Column<int>(type: "integer", nullable: false),
                run_limit = table.Column<TimeSpan>(type: "interval", nullable: true),
                outcome_kind = table.Column<int>(type: "integer", nullable: true),
                outcome_code = table.Column<string>(type: "text", nullable: true),
                outcome = table.Column<byte[]>(type: "bytea", nullable: true),
                ended_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_commands", columns => columns.id);
                table.ForeignKey(
                    name: "FK_commands_agents_agent_id",
                    column: columns => columns.agent_id,
                    principalSchema: _schema,
                    principalTable: "agents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });

        migrationBuilder.CreateIndex(
            name: "IX_commands_agent_id_sequence",
            schema: _schema,
            table: "commands",
            columns: ["agent_id", "sequence"],
            filter: "outcome_kind IS NULL");

        migrationBuilder.CreateIndex(
            name: "IX_commands_base_key_caller_key",
            schema: _schema,
            table: "commands",
            columns: ["base_key", "caller_key"],
            unique: true,
            filter: "caller_key IS NOT NULL AND (outcome_kind IS NULL OR outcome_kind NOT IN (3, 5))");

        migrationBuilder.CreateIndex(
            name: "IX_commands_ended_at",
            schema: _schema,
            table: "commands",
            column: "ended_at");
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

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.CommandRow", command =>
        {
            command.Property<Guid>("Id")
                .HasColumnType("uuid")
                .HasColumnName("id");

            command.Property<Guid>("AgentId")
                .HasColumnType("uuid")
                .HasColumnName("agent_id");

            command.Property<string>("BaseKey")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("base_key");

            command.Property<byte[]>("Bytes")
                .IsRequired()
                .HasColumnType("bytea")
                .HasColumnName("bytes");

            command.Property<string>("CallerKey")
                .HasColumnType("text")
                .HasColumnName("caller_key");

            command.Property<DateTimeOffset>("Deadline")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("deadline");

            command.Property<DateTimeOffset?>("EndedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("ended_at");

            command.Property<int>("Mode")
                .HasColumnType("integer")
                .HasColumnName("mode");

            command.Property<byte[]>("Outcome")
                .HasColumnType("bytea")
                .HasColumnName("outcome");

            command.Property<string>("OutcomeCode")
                .HasColumnType("text")
                .HasColumnName("outcome_code");

            command.Property<int?>("OutcomeKind")
                .HasColumnType("integer")
                .HasColumnName("outcome_kind");

            command.Property<int>("OutcomeLimit")
                .HasColumnType("integer")
                .HasColumnName("outcome_limit");

            command.Property<int>("Phase")
                .HasColumnType("integer")
                .HasColumnName("phase");

            command.Property<TimeSpan?>("RunLimit")
                .HasColumnType("interval")
                .HasColumnName("run_limit");

            command.Property<long>("Sequence")
                .ValueGeneratedOnAdd()
                .HasColumnType("bigint")
                .HasColumnName("sequence");

            NpgsqlPropertyBuilderExtensions.UseIdentityByDefaultColumn(command.Property<long>("Sequence"));

            command.Property<byte[]>("Signature")
                .IsRequired()
                .HasColumnType("bytea")
                .HasColumnName("signature");

            command.Property<TimeSpan>("Skew")
                .HasColumnType("interval")
                .HasColumnName("skew");

            command.HasKey("Id");

            command.HasIndex("EndedAt");

            command.HasIndex("AgentId", "Sequence")
                .HasFilter("outcome_kind IS NULL");

            command.HasIndex("BaseKey", "CallerKey")
                .IsUnique()
                .HasFilter("caller_key IS NOT NULL AND (outcome_kind IS NULL OR outcome_kind NOT IN (3, 5))");

            command.ToTable("commands", _schema);
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

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.CommandRow", command =>
        {
            command.HasOne("Moltaro.OnecOutpost.Server.Store.AgentRow", null)
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
