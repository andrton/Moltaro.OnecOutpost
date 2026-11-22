using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Server.Tests.Commands;

// WriteAsync applies the rules of names to each property of its request, as ReadAsync does, and requires a caller key.
// A refusal names the property.
public sealed class WriteArgumentTests : IAsyncDisposable
{
    private readonly ServiceProvider _services = ServerSetup.Create();

    private IOutpostServer Server => _services.GetRequiredService<IOutpostServer>();

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public void A_write_within_every_rule_passes_the_checks()
    {
        OutpostWriteRequest request = new()
        {
            BaseKey = "accounting",
            CallerKey = "invoice:2026/42",
            Script = "Result = Parameters.Get(\"number\");",
            Parameters = JsonNode.Parse("""{ "number": "2026/42", "lines": [ { "amount": 10.5 } ] }""")!.AsObject(),
        };

        ArgumentChecks.AssertPassed(() => Server.WriteAsync(request, ArgumentChecks.Cancelled));
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
            nameof(OutpostWriteRequest.BaseKey),
            () => Call(new() { BaseKey = null!, CallerKey = "invoice-42", Script = "Result = 1;" }));
    }

    [Fact]
    public void A_base_key_that_breaks_its_rule_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostWriteRequest.BaseKey),
            () => Call(new() { BaseKey = "Accounting", CallerKey = "invoice-42", Script = "Result = 1;" }));
    }

    [Fact]
    public void A_write_without_a_caller_key_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentNullException>(
            nameof(OutpostWriteRequest.CallerKey),
            () => Call(new() { BaseKey = "accounting", CallerKey = null!, Script = "Result = 1;" }));
    }

    [Fact]
    public void A_caller_key_that_breaks_its_rule_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostWriteRequest.CallerKey),
            () => Call(new() { BaseKey = "accounting", CallerKey = "two keys", Script = "Result = 1;" }));
    }

    [Fact]
    public void A_null_script_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentNullException>(
            nameof(OutpostWriteRequest.Script),
            () => Call(new() { BaseKey = "accounting", CallerKey = "invoice-42", Script = null! }));
    }

    [Fact]
    public void A_script_that_breaks_its_rule_is_refused_with_the_name_of_its_property()
    {
        Assert.Throws<ArgumentException>(
            nameof(OutpostWriteRequest.Script),
            () => Call(new() { BaseKey = "accounting", CallerKey = "invoice-42", Script = " \n " }));
    }

    [Fact]
    public void Parameters_with_a_string_that_holds_U0000_are_refused_with_the_name_of_their_property()
    {
        OutpostWriteRequest request = new()
        {
            BaseKey = "accounting",
            CallerKey = "invoice-42",
            Script = "Result = 1;",
            Parameters = JsonNode.Parse("""{ "text": "a\u0000b" }""")!.AsObject(),
        };

        Assert.Throws<ArgumentException>(nameof(OutpostWriteRequest.Parameters), () => Call(request));
    }

    // The places of a name given twice are stated in full through ReadAsync, which shares the check.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void Parameters_with_a_name_twice_in_one_object_are_refused_with_the_name_of_their_property()
    {
        OutpostWriteRequest request = new()
        {
            BaseKey = "accounting",
            CallerKey = "invoice-42",
            Script = "Result = 1;",
            Parameters = JsonNode.Parse("""{ "number": "2026/42", "number": "2026/43" }""")!.AsObject(),
        };

        Assert.Throws<ArgumentException>(nameof(OutpostWriteRequest.Parameters), () => Call(request));
    }

    // The limits of writing the parameters as JSON are stated in full through ReadAsync, which shares the check.
    [Fact]
    public void Parameters_nested_65_levels_are_refused_with_the_name_of_their_property()
    {
        OutpostWriteRequest request = new()
        {
            BaseKey = "accounting",
            CallerKey = "invoice-42",
            Script = "Result = 1;",
            Parameters = NestedParameters.Create(65),
        };

        Assert.Throws<ArgumentException>(nameof(OutpostWriteRequest.Parameters), () => Call(request));
    }

    // The checks are thrown by the call itself, before it returns a task.
    private void Call(OutpostWriteRequest request)
    {
        _ = Server.WriteAsync(request, TestContext.Current.CancellationToken);
    }
}
