using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Arguments;

// The arguments of secret <BASE> <KIND>: BASE a base key by its rule, and KIND exactly read,
// write or worker. Anything else ends secret with Usage before it runs, naming the rule.
public sealed class SecretArgumentTests
{
    private const string BaseRule =
        "<BASE>: expected a base key: 1 to 64 characters of a-z, 0-9, -, _ and ., the first a letter or a digit";

    private const string KindRule = "<KIND>: expected read, write or worker";

    private readonly TestMachine _machine = new();

    [Theory]
    [InlineData("Accounting", "read", BaseRule)]
    [InlineData("_accounting", "read", BaseRule)]
    [InlineData("accounting", "Read", KindRule)]
    [InlineData("accounting", "password", KindRule)]
    public async Task A_base_key_or_a_kind_out_of_its_rule_ends_secret_with_Usage_naming_the_rule(string baseKey, string kind, string rule)
    {
        CommandAppResult result = await _machine.RunAsync("secret", baseKey, kind);

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Equal(["Usage (2): " + rule], TestMachine.LinesOf(result));
        Assert.Empty(_machine.Operations);
    }

    [Theory]
    [InlineData("read")]
    [InlineData("write")]
    [InlineData("worker")]
    public async Task A_base_key_with_each_kind_is_accepted(string kind)
    {
        CommandAppResult result = await _machine.RunAsync("secret", "accounting", kind);

        Assert.NotEqual(ConsoleExitCodes.Usage, result.ExitCode);
        SecretCommand.Settings settings = Assert.IsType<SecretCommand.Settings>(result.Settings);
        Assert.Equal("accounting", settings.Base);
        Assert.Equal(kind, settings.Kind);
    }
}
