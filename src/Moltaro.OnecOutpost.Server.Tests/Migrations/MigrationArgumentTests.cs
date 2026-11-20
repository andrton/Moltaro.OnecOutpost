namespace Moltaro.OnecOutpost.Server.Tests.Migrations;

// The migrations apply the rule of schemas, which the tests of the options state in full. A refusal names the
// parameter, and it is thrown by the call itself.
public sealed class MigrationArgumentTests
{
    private const string ConnectionString = "Host=localhost;Database=outpost_tests";

    [Fact]
    public void MigrateAsync_refuses_a_null_connection_string()
    {
        Assert.Throws<ArgumentNullException>(
            "connectionString",
            () => { _ = OutpostMigrations.MigrateAsync(null!, "outpost", TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void MigrateAsync_refuses_a_null_schema()
    {
        Assert.Throws<ArgumentNullException>(
            "schema",
            () => { _ = OutpostMigrations.MigrateAsync(ConnectionString, null!, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void MigrateAsync_refuses_a_schema_that_breaks_its_rule()
    {
        Assert.Throws<ArgumentException>(
            "schema",
            () => { _ = OutpostMigrations.MigrateAsync(ConnectionString, "Outpost", TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void GetScripts_refuses_a_null_schema()
    {
        Assert.Throws<ArgumentNullException>("schema", () => OutpostMigrations.GetScripts(null!));
    }

    [Fact]
    public void GetScripts_refuses_a_schema_that_breaks_its_rule()
    {
        Assert.Throws<ArgumentException>("schema", () => OutpostMigrations.GetScripts("Outpost"));
    }
}
