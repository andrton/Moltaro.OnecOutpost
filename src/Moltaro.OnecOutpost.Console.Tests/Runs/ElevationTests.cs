using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Runs;

// The first step of every command (the S10 brief, "install", step 1, and "The steps pair and secret share"): a console
// that is not run as administrator, UAC's filtered token included, ends the command with NotElevated before anything is
// asked of the machine, on a machine where every step would change something.
public sealed class ElevationTests
{
    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    [InlineData("pair")]
    [InlineData("secret")]
    [InlineData("check")]
    [InlineData("metadata", Skip = SkipReason.NotImplemented)]
    [InlineData("connector register")]
    public async Task A_command_in_a_console_that_is_not_elevated_ends_with_NotElevated_and_changes_nothing(string command)
    {
        TestMachine machine = new() { Elevated = false };
        machine.Installed();
        machine.HasService();
        machine.Input = new FakeInput(machine.Data, "a password\n");
        machine.Connectors.Installed(ConnectorBitnessEnum.X64, "8.3.27.1859");

        CommandAppResult result = await machine.RunAsync(TestMachine.CommandLine(command));

        Assert.Equal(ConsoleExitCodes.NotElevated, result.ExitCode);
        Assert.Contains(
            $"NotElevated (3): {command} needs a console run as administrator; nothing was changed",
            TestMachine.LinesOf(result));
        Assert.Empty(machine.Operations);
        Assert.Empty(machine.Routes);
    }
}
