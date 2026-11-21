using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Moltaro.OnecOutpost.Server.Store.Migrations;

// The first migration of the store: the agents and their pairing tokens. Scaffolded by the EF Core tools for one schema;
// the schema was then made a parameter by hand, so that the migration serves the schema it is created with. Its target
// model is the model of the store as this migration leaves it.
[Migration(Id)]
internal sealed class AgentsAndPairingTokens : Migration
{
    public const string Id = "20261121194747_AgentsAndPairingTokens";

    private readonly string _schema;

    public AgentsAndPairingTokens(string schema)
    {
        _schema = schema;
    }

    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.EnsureSchema(name: _schema);

        migrationBuilder.CreateTable(
            name: "agents",
            schema: _schema,
            columns: table => new
            {
                id = table.Column<Guid>(type: "uuid", nullable: false),
                name = table.Column<string>(type: "text", nullable: false),
                state = table.Column<int>(type: "integer", nullable: false),
                created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                paired_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                public_key = table.Column<byte[]>(type: "bytea", nullable: true),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_agents", columns => columns.id);
            });

        migrationBuilder.CreateTable(
            name: "pairing_tokens",
            schema: _schema,
            columns: table => new
            {
                agent_id = table.Column<Guid>(type: "uuid", nullable: false),
                secret = table.Column<byte[]>(type: "bytea", nullable: false),
                expires_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_pairing_tokens", columns => columns.agent_id);
                table.ForeignKey(
                    name: "FK_pairing_tokens_agents_agent_id",
                    column: columns => columns.agent_id,
                    principalSchema: _schema,
                    principalTable: "agents",
                    principalColumn: "id",
                    onDelete: ReferentialAction.Cascade);
            });
    }

    protected override void BuildTargetModel(ModelBuilder modelBuilder)
    {
        modelBuilder
            .HasDefaultSchema(_schema)
            .HasAnnotation("ProductVersion", "10.0.15")
            .HasAnnotation("Relational:MaxIdentifierLength", 63);

        NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

        modelBuilder.Entity("Moltaro.OnecOutpost.Server.Store.AgentRow", agent =>
        {
            agent.Property<Guid>("Id")
                .HasColumnType("uuid")
                .HasColumnName("id");

            agent.Property<DateTimeOffset>("CreatedAt")
                .HasColumnType("timestamp with time zone")
                .HasColumnName("created_at");

            agent.Property<string>("Name")
                .IsRequired()
                .HasColumnType("text")
                .HasColumnName("name");

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
