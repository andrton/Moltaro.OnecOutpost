using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.OnecOutpost.Server.Tests.Bases;

// ReleaseBaseAsync applies the rule of base keys, which the tests of the protocol state in full.
public sealed class ReleaseBaseArgumentTests : IAsyncDisposable
{
    private readonly ServiceProvider _services = ServerSetup.Create();

    private IOutpostServer Server => _services.GetRequiredService<IOutpostServer>();

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public void A_base_key_within_its_rule_passes_the_checks()
    {
        ArgumentChecks.AssertPassed(() => Server.ReleaseBaseAsync("accounting", ArgumentChecks.Cancelled));
    }

    [Fact]
    public void A_null_base_key_is_refused_with_the_name_of_its_parameter()
    {
        Assert.Throws<ArgumentNullException>(
            "baseKey",
            () => { _ = Server.ReleaseBaseAsync(null!, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void A_base_key_that_breaks_its_rule_is_refused_with_the_name_of_its_parameter()
    {
        Assert.Throws<ArgumentException>(
            "baseKey",
            () => { _ = Server.ReleaseBaseAsync("Accounting", TestContext.Current.CancellationToken); });
    }
}
