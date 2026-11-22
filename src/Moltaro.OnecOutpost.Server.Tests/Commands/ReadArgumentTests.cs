using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Moltaro.OnecOutpost.TestSupport;

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

    // In the JSON text of the third, \u0070 is an escape of the letter p.
    public static TheoryData<string> ParametersWithANameTwice =>
    [
        """{ "period": "2026-11", "period": "2026-12" }""",
        """{ "lines": [ { "item": 1, "item": 2 } ] }""",
        "{ \"period\": \"2026-11\", \"\\u0070eriod\": \"2026-12\" }",
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

    // A JsonObject parsed from JSON text keeps that text until it is changed, so a name given twice reaches the call.
    // jsonb keeps the last value of such a name, so a repeat would compare equal to parameters other than those signed
    // (concept rule 4). The places are the outer object, an object nested in an array, and a name that is the same as
    // the other only once its escape is read.
    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(ParametersWithANameTwice))]
    public void Parameters_with_a_name_twice_in_one_object_are_refused_with_the_name_of_their_property(string parameters)
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            Script = "Result = 1;",
            Parameters = JsonNode.Parse(parameters)!.AsObject(),
        };

        Assert.Throws<ArgumentException>(nameof(OutpostReadRequest.Parameters), () => Call(request));
    }

    // A caller builds the parameters from .NET strings, which System.Text.Json would write with U+FFFD in place of a
    // lone surrogate without a word, or parses them from JSON text, where a lone surrogate is an escape. The rule holds
    // for every string of the parameters, so the places are a string, a nested string and a member name, and a string
    // parsed from JSON.
    [Theory]
    [InlineData("a string")]
    [InlineData("a nested string")]
    [InlineData("a member name")]
    [InlineData("a string parsed from JSON")]
    public void Parameters_with_a_string_that_holds_a_lone_surrogate_are_refused_with_the_name_of_their_property(string place)
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            Script = "Result = 1;",
            Parameters = WithLoneSurrogate(place),
        };

        Assert.Throws<ArgumentException>(nameof(OutpostReadRequest.Parameters), () => Call(request));
    }

    // The emoji U+1F600 is a surrogate pair, which UTF-8 carries: the rule refuses only a half without the other.
    [Fact]
    public void A_read_with_surrogate_pairs_in_its_script_and_parameters_passes_the_checks()
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            Script = "Result = \"\U0001F600\";",
            Parameters = new JsonObject { ["\U0001F600"] = "Entrep\U0001F600t" },
        };

        ArgumentChecks.AssertPassed(() => Server.ReadAsync(request, ArgumentChecks.Cancelled));
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

    // A JsonValue may hold any .NET value, and System.Text.Json writes no System.Type.
    [Fact]
    public void Parameters_with_a_value_that_JSON_cannot_write_are_refused_with_the_name_of_their_property()
    {
        OutpostReadRequest request = new()
        {
            BaseKey = "accounting",
            Script = "Result = 1;",
            Parameters = new JsonObject { ["type"] = JsonValue.Create(typeof(string)) },
        };

        Assert.Throws<ArgumentException>(nameof(OutpostReadRequest.Parameters), () => Call(request));
    }

    // Parameters with a half of the emoji U+1F600 without the other, its high surrogate or its low one, at the place a
    // test names. In C#, "\uD83D" is the surrogate itself; in the JSON text, \uDE00 is an escape.
    private static JsonObject WithLoneSurrogate(string place)
    {
        return place switch
        {
            "a string" => new JsonObject { ["text"] = "Entrep\uD83D" },
            "a nested string" => new JsonObject { ["lines"] = new JsonArray(new JsonObject { ["item"] = "\uDE00t" }) },
            "a member name" => new JsonObject { ["na\uD83Dme"] = 1 },
            "a string parsed from JSON" => JsonNode.Parse("""{ "text": "\uDE00t" }""")!.AsObject(),
            _ => throw new ArgumentOutOfRangeException(nameof(place), place, "Not a place of the parameters."),
        };
    }

    // The checks are thrown by the call itself, before it returns a task.
    private void Call(OutpostReadRequest request)
    {
        _ = Server.ReadAsync(request, TestContext.Current.CancellationToken);
    }
}
