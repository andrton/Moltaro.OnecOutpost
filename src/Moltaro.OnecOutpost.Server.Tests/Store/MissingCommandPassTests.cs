using Moltaro.OnecOutpost.Server.Store;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Store;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

[Trait("Requires", "PostgreSQL")]
public sealed class MissingCommandPassTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task A_pass_of_a_missing_command_returns_null()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        MigratedSchema schema = await MigratedSchema.CreateAsync(postgres, "pass_missing_command", cancellationToken);
        OutpostStore store = new(schema.ConnectionString, schema.Name, new FakeClock());

        Assert.Null(await store.PassCommandAsync(Guid.NewGuid(), cancellationToken));
    }
}
