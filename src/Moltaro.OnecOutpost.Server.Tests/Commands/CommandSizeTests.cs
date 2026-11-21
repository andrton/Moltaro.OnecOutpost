using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;

namespace Moltaro.OnecOutpost.Server.Tests.Commands;

// The size of a command is the length of the text of its script between its quotes plus that of the text of its
// parameters, both in UTF-8 as the command carries them: every escape counted as it is written, the characters
// outside ASCII as they are, not escaped, and the 2 bytes of {} for a command without parameters (protocol §7). A
// command up to CommandSizeLimit passes; a larger one is refused. The rule is stated in full through ReadAsync;
// WriteAsync shows that it applies it.
public sealed class CommandSizeTests : IAsyncDisposable
{
    private const int Limit = 4 * 1024;

    // What the limit leaves for the script of a command without parameters: the limit less the 2 bytes of {}.
    private const int ScriptLimit = Limit - 2;

    // U+00E9: 2 bytes in UTF-8; escaped as \u00e9 it would count 6, and as a UTF-16 unit 1.
    private const string TwoBytes = "\u00e9";

    // U+1F600: 4 bytes in UTF-8; escaped as \ud83d\ude00 it would count 12, and as UTF-16 units 2.
    private const string FourBytes = "\U0001F600";

    private readonly ServiceProvider _services = ServerSetup.Create(options => options.CommandSizeLimit = Limit);

    private IOutpostServer Server => _services.GetRequiredService<IOutpostServer>();

    public ValueTask DisposeAsync() => _services.DisposeAsync();

    [Fact]
    public void A_script_of_exactly_the_limit_in_UTF8_passes_the_checks()
    {
        OutpostReadRequest request = new() { BaseKey = "accounting", Script = Repeat(TwoBytes, ScriptLimit / 2) };

        ArgumentChecks.AssertPassed(() => Server.ReadAsync(request, ArgumentChecks.Cancelled));
    }

    [Fact]
    public void A_script_one_byte_over_the_limit_in_UTF8_is_refused()
    {
        OutpostReadRequest request = new() { BaseKey = "accounting", Script = Repeat(TwoBytes, ScriptLimit / 2) + "a" };

        Assert.Throws<ArgumentException>(() => { _ = Server.ReadAsync(request, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void Parameters_that_fill_the_limit_without_escapes_pass_the_checks()
    {
        OutpostReadRequest request = WithText(Repeat(FourBytes, (Limit - 12) / 4));

        ArgumentChecks.AssertPassed(() => Server.ReadAsync(request, ArgumentChecks.Cancelled));
    }

    [Fact]
    public void Parameters_one_byte_over_the_limit_without_escapes_are_refused()
    {
        OutpostReadRequest request = WithText(Repeat(FourBytes, (Limit - 12) / 4) + "a");

        Assert.Throws<ArgumentException>(() => { _ = Server.ReadAsync(request, TestContext.Current.CancellationToken); });
    }

    [Fact]
    public void A_write_one_byte_over_the_limit_is_refused()
    {
        OutpostWriteRequest request = new()
        {
            BaseKey = "accounting",
            CallerKey = "invoice-42",
            Script = Repeat(TwoBytes, ScriptLimit / 2) + "a",
        };

        Assert.Throws<ArgumentException>(() => { _ = Server.WriteAsync(request, TestContext.Current.CancellationToken); });
    }

    // A read with a script of 1 byte and the parameters {"text":"..."}, whose JSON adds 11 bytes around the text: 12
    // bytes besides the text.
    private static OutpostReadRequest WithText(string text)
    {
        return new OutpostReadRequest
        {
            BaseKey = "accounting",
            Script = "a",
            Parameters = new JsonObject { ["text"] = text },
        };
    }

    private static string Repeat(string text, int count)
    {
        return string.Concat(Enumerable.Repeat(text, count));
    }
}
