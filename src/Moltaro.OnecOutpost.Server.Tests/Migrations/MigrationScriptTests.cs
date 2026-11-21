using Microsoft.EntityFrameworkCore;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server.Tests.Migrations;

// The scripts of the migrations come from the library alone (the brief of S3, "Contract"): no test here has a database,
// and every lane runs them.
public sealed class MigrationScriptTests
{
    private const string Schema = "scripts_without_database";

    [Fact]
    public void GetScripts_works_without_a_database()
    {
        IReadOnlyList<OutpostMigrationScript> scripts = OutpostMigrations.GetScripts(Schema);

        Assert.NotEmpty(scripts);
        Assert.All(scripts, script => Assert.Contains(Schema, script.Sql, StringComparison.Ordinal));
    }

    // A script records the identifier of its migration as applied, so its SQL holds its name.
    [Fact]
    public void The_name_of_each_script_is_the_identifier_of_its_migration()
    {
        using StoreContext context = StoreContextFactory.Create(null, Schema);

        IReadOnlyList<OutpostMigrationScript> scripts = OutpostMigrations.GetScripts(Schema);

        Assert.NotEmpty(scripts);
        Assert.Equal(context.Database.GetMigrations(), scripts.Select(script => script.Name));
        Assert.All(scripts, script => Assert.Contains(script.Name, script.Sql, StringComparison.Ordinal));
    }
}
