using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Arguments;

// The arguments of pair <SERVER> <TOKEN>: SERVER an absolute address that §11 of
// docs/protocol.md allows, https or http on loopback as ServerRoute judges it, and TOKEN a pairing token of §5. Anything
// else ends pair with Usage before it runs, naming the rule and quoting neither argument, since the token holds a secret
// and a mistyped server may hold one too.
public sealed class PairArgumentTests
{
    private const string ServerRule = "<SERVER>: expected an https address, or an http one on loopback (§11 of the protocol)";

    private const string TokenRule = "<TOKEN>: expected a pairing token as the application gave it";

    private readonly TestMachine _machine = new();

    // A token whose fingerprint is one digit short of the form of §5.
    private static string ShortToken => TestMachine.Token[..^1];

    [Theory]
    [InlineData("outpost.example.com/agents/", "token", ServerRule)]
    [InlineData("http://outpost.example.com/agents/", "token", ServerRule)]
    [InlineData("https://outpost.example.com/agents/", "short", TokenRule)]
    public async Task A_server_or_a_token_out_of_its_rule_ends_pair_with_Usage_naming_the_rule_and_quoting_neither(
        string server,
        string token,
        string rule)
    {
        string tokenText = token == "token" ? TestMachine.Token : ShortToken;

        CommandAppResult result = await _machine.RunAsync("pair", server, tokenText);

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Equal(["Usage (2): " + rule], TestMachine.LinesOf(result));
        Assert.DoesNotContain(server, result.Output);
        Assert.DoesNotContain(ShortToken, result.Output);
        Assert.Empty(_machine.Operations);
    }

    [Theory]
    [InlineData("https://outpost.example.com/agents/")]
    [InlineData("http://127.0.0.1:5000/agents/")]
    [InlineData("http://localhost:5000/agents/")]
    public async Task An_https_server_or_an_http_one_on_loopback_with_a_token_is_accepted(string server)
    {
        CommandAppResult result = await _machine.RunAsync("pair", server, TestMachine.Token);

        Assert.NotEqual(ConsoleExitCodes.Usage, result.ExitCode);
        PairCommand.Settings settings = Assert.IsType<PairCommand.Settings>(result.Settings);
        Assert.Equal(server, settings.Server);
        Assert.Equal(TestMachine.Token, settings.Token);
    }
}
