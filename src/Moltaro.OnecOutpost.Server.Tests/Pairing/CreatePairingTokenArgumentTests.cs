using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.OnecOutpost.Server.Tests.Pairing;

// CreatePairingTokenAsync applies the rule of agent names, which the tests of the protocol state in full.
public sealed class CreatePairingTokenArgumentTests : IAsyncDisposable
{
    private readonly ServiceProvider _services = ServerSetup.Create();

    private IOutpostServer Server => _services.GetRequiredService<IOutpostServer>();

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public void An_agent_name_within_its_rule_passes_the_checks()
    {
        ArgumentChecks.AssertPassed(() => Server.CreatePairingTokenAsync("Agent of the warehouse", ArgumentChecks.Cancelled));
    }

    [Fact]
    public void A_null_agent_name_is_refused_with_the_name_of_its_parameter()
    {
        Assert.Throws<ArgumentNullException>(
            "agentName",
            () => { _ = Server.CreatePairingTokenAsync(null!, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void An_agent_name_that_breaks_its_rule_is_refused_with_the_name_of_its_parameter()
    {
        Assert.Throws<ArgumentException>(
            "agentName",
            () => { _ = Server.CreatePairingTokenAsync(" agent", TestContext.Current.CancellationToken); });
    }
}
