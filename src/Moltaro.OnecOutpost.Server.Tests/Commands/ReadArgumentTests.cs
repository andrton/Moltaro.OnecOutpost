using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.OnecOutpost.Server.Tests.Commands;

// ReadAsync applies the rules of names to each property of its request; the rules themselves are stated in full by the
// tests of the protocol, and the rule of parameters here. A refusal names the property.
public sealed class ReadArgumentTests : IAsyncDisposable
{
    private readonly ServiceProvider _services = ServerSetup.Create();

    // Strings at three depths, a member name among them: the rule holds for every string of the parameters.
    public static TheoryData<string> ParametersWithU0000 =>
    [
        """{ "text": "a\u0000b" }""",
        """{ "lines": [ { "item": "a\u0000" } ] }""",
        """{ "na\u0000me": 1 }""",
    ];

    private IOutpostServer Server => _services.GetRequiredService<IOutpostServer>();

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public void A_read_within_every_rule_passes_the_checks()
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            CallerKey = "report:2026-11",
            Script = "Result = Parameters.Get(\"period\");",
            Parameters = JsonNode.Parse("""{ "period": "2026-11", "items": [ 1, 2.5, true, null, { "name": "Entrep\u00f4t" } ] }""")!.AsObject(),
        };

        ArgumentChecks.AssertPassed(() => Server.ReadAsync(request, ArgumentChecks.Cancelled));
    }

    [Fact]
    public void A_read_without_a_caller_key_passes_the_checks()
    {
        OutpostReadRequest request = new() { BaseKey = "accounting", Script = "Result = 1;" };

        ArgumentChecks.AssertPassed(() => Server.ReadAsync(request, ArgumentChecks.Cancelled));
    }

    [Fact]
    public void A_null_request_is_refused()
    {
        Assert.Throws<ArgumentNullException>("request", () => Call(null!));
    }

    [Fact]
    public void A_null_base_key_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentNullException>(
            nameof(OutpostReadRequest.BaseKey),
            () => Call(new() { BaseKey = null!, Script = "Result = 1;" }));
    }

    [Fact]
    public void A_base_key_that_breaks_its_rule_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostReadRequest.BaseKey),
            () => Call(new() { BaseKey = "Accounting", Script = "Result = 1;" }));
    }

    [Fact]
    public void A_caller_key_that_breaks_its_rule_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostReadRequest.CallerKey),
            () => Call(new() { BaseKey = "accounting", CallerKey = "two keys", Script = "Result = 1;" }));
    }

    [Fact]
    public void A_null_script_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentNullException>(
            nameof(OutpostReadRequest.Script),
            () => Call(new() { BaseKey = "accounting", Script = null! }));
    }

    [Fact]
    public void A_script_that_breaks_its_rule_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostReadRequest.Script),
            () => Call(new() { BaseKey = "accounting", Script = " \n " }));
    }

    [Theory]
    [MemberData(nameof(ParametersWithU0000))]
    public void Parameters_with_a_string_that_holds_U0000_are_refused_with_the_name_of_their_property(string parameters)
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            Script = "Result = 1;",
            Parameters = JsonNode.Parse(parameters)!.AsObject(),
        };

        Assert.Throws<ArgumentException>(nameof(OutpostReadRequest.Parameters), () => Call(request));
    }

    [Fact]
    public void Parameters_nested_64_levels_pass_the_checks()
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            Script = "Result = 1;",
            Parameters = NestedParameters.Create(64),
        };

        ArgumentChecks.AssertPassed(() => Server.ReadAsync(request, ArgumentChecks.Cancelled));
    }

    [Fact]
    public void Parameters_nested_65_levels_are_refused_with_the_name_of_their_property()
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            Script = "Result = 1;",
            Parameters = NestedParameters.Create(65),
        };

        Assert.Throws<ArgumentException>(nameof(OutpostReadRequest.Parameters), () => Call(request));
    }

    // JSON has no number that is not finite, so such parameters cannot be written.
    [Fact]
    public void Parameters_with_a_number_that_is_not_finite_are_refused_with_the_name_of_their_property()
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            Script = "Result = 1;",
            Parameters = new JsonObject { ["ratio"] = JsonValue.Create(double.NaN) },
        };

        Assert.Throws<ArgumentException>(nameof(OutpostReadRequest.Parameters), () => Call(request));
    }

    // The checks are thrown by the call itself, before it returns a task.
    private void Call(OutpostReadRequest request)
    {
        _ = Server.ReadAsync(request, TestContext.Current.CancellationToken);
    }
}
