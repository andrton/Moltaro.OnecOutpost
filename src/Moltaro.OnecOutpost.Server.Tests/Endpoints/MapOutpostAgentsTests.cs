using Microsoft.AspNetCore.Builder;

namespace Moltaro.OnecOutpost.Server.Tests.Endpoints;

// The rule of the endpoint prefix, stated in full, and the registration that mapping needs.
public sealed class MapOutpostAgentsTests
{
    public static TheoryData<string> GoodPrefixes =>
    [
        "/outpost",
        "/api/v2/outpost-agents",
        "/A-Z_a.z0-9",
        "/.well-known",
    ];

    public static TheoryData<string> BrokenPrefixes =>
    [
        "",
        "outpost",
        "/",
        "/outpost/",
        "/api//outpost",
        "/.",
        "/api/..",
        "/out post",
        "/caf\u00e9",
    ];

    [Theory]
    [MemberData(nameof(GoodPrefixes))]
    public async Task A_slash_followed_by_segments_of_letters_digits_hyphens_underscores_and_dots_is_a_prefix(string prefix)
    {
        await using WebApplication app = CreateApplication(withServer: true);

        ArgumentChecks.AssertPassed(() => app.MapOutpostAgents(prefix));
    }

    [Theory]
    [MemberData(nameof(BrokenPrefixes))]
    public async Task Any_other_prefix_is_refused_with_the_name_of_its_parameter(string prefix)
    {
        await using WebApplication app = CreateApplication(withServer: true);

        Assert.Throws<ArgumentException>("prefix", () => app.MapOutpostAgents(prefix));
    }

    [Fact]
    public async Task A_null_prefix_is_refused_with_the_name_of_its_parameter()
    {
        await using WebApplication app = CreateApplication(withServer: true);

        Assert.Throws<ArgumentNullException>("prefix", () => app.MapOutpostAgents(null!));
    }

    [Fact]
    public async Task Mapping_without_AddOutpostServer_is_refused()
    {
        await using WebApplication app = CreateApplication(withServer: false);

        Assert.Throws<InvalidOperationException>(() => app.MapOutpostAgents("/outpost"));
    }

    private static WebApplication CreateApplication(bool withServer)
    {
        WebApplicationBuilder builder = WebApplication.CreateSlimBuilder();
        ServerSetup.AddSignals(builder.Services);

        if (withServer)
        {
            builder.Services.AddOutpostServer(ServerSetup.Require);
        }

        return builder.Build();
    }
}
