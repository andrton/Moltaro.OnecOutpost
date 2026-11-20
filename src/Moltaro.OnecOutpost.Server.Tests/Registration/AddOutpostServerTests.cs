using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.OnecOutpost.Server.Tests.Registration;

public sealed class AddOutpostServerTests
{
    [Fact]
    public async Task The_server_is_one_instance_for_the_whole_container()
    {
        await using ServiceProvider services = ServerSetup.Create();

        IOutpostServer first = services.GetRequiredService<IOutpostServer>();
        IOutpostServer second = services.GetRequiredService<IOutpostServer>();

        Assert.Same(first, second);
    }

    [Fact]
    public void A_second_call_on_the_same_collection_is_refused()
    {
        ServiceCollection services = new();
        services.AddOutpostServer(ServerSetup.Require);

        Assert.Throws<InvalidOperationException>(() => services.AddOutpostServer(ServerSetup.Require));
    }

    // The library never registers an ISignals of its own: the application provides it.
    [Fact]
    public async Task Resolving_the_server_without_an_ISignals_is_refused_with_a_message_that_says_so()
    {
        ServiceCollection services = new();
        services.AddOutpostServer(ServerSetup.Require);

        await using ServiceProvider provider = services.BuildServiceProvider();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
            () => provider.GetRequiredService<IOutpostServer>());

        Assert.Contains("ISignals", exception.Message, StringComparison.Ordinal);
    }
}
