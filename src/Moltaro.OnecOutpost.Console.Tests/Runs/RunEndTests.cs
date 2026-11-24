using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Runs;

// How a run of the console ends, whatever its command (the S10 brief, "The program" and "Output"): ConsoleApp parses
// strictly and ends Spectre's parse and validation errors with Usage, an OperationCanceledException whose token is
// cancelled with Cancelled, and any other exception with Failure, each written as {Kind} ({code}): {reason}. --help ends
// with Success. Ctrl+C cancels the token of the run, which each command checks before each of its steps.
public sealed class RunEndTests
{
    private readonly TestMachine _machine = new();

    [Theory]
    [InlineData("frobnicate")]
    [InlineData("install", "--force")]
    [InlineData("pair", "https://outpost.example.com/agents/")]
    [InlineData("secret", "accounting")]
    [InlineData("metadata", "accounting")]
    public async Task An_unknown_command_or_option_or_a_missing_argument_ends_with_Usage_and_changes_nothing(params string[] arguments)
    {
        CommandAppResult result = await _machine.RunAsync(arguments);

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.StartsWith("Usage (2): ", result.Output);
        Assert.Empty(_machine.Operations);
    }

    [Fact]
    public async Task Help_ends_with_Success_and_names_every_command()
    {
        CommandAppResult result = await _machine.RunAsync("--help");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Contains("install", result.Output);
        Assert.Contains("uninstall", result.Output);
        Assert.Contains("pair", result.Output);
        Assert.Contains("secret", result.Output);
        Assert.Contains("check", result.Output);
        Assert.Contains("metadata", result.Output);
        Assert.Contains("connector", result.Output);
        Assert.Empty(_machine.Operations);
    }

    // The help of install, uninstall and check describes --stop-timeout and --clock-skew with [d.]hh:mm:ss, which
    // Spectre reads as markup, so the description escapes it.
    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    [InlineData("pair")]
    [InlineData("secret")]
    [InlineData("check")]
    [InlineData("metadata")]
    [InlineData("connector", "register")]
    public async Task The_help_of_each_command_ends_with_Success_and_changes_nothing(params string[] command)
    {
        CommandAppResult result = await _machine.RunAsync([.. command, "--help"]);

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Contains("USAGE:", result.Output);
        Assert.Empty(_machine.Operations);
    }

    // A branch runs only its commands, so connector alone is a usage error, as a missing argument is; Spectre would show
    // the help of the branch and end with 1, the code of Failure.
    [Fact]
    public async Task Connector_without_its_command_ends_with_Usage_and_changes_nothing()
    {
        CommandAppResult result = await _machine.RunAsync("connector");

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.StartsWith("Usage (2): ", result.Output);
        Assert.Contains("connector register", result.Output);
        Assert.Empty(_machine.Operations);
    }

    [Fact]
    public async Task The_help_of_connector_ends_with_Success_and_names_its_command()
    {
        CommandAppResult result = await _machine.RunAsync("connector", "--help");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Contains("USAGE:", result.Output);
        Assert.Contains("register", result.Output);
        Assert.Empty(_machine.Operations);
    }

    // An exception that no step expects, such as one of a boundary that breaks its own contract.
    [Fact]
    public async Task An_unexpected_exception_ends_the_command_with_Failure_and_its_message_and_nothing_after()
    {
        _machine.Services.StopFailure = new InvalidOperationException("the fake service controller broke");

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): ", result.Output);
        Assert.Contains("the fake service controller broke", result.Output);
        Assert.Equal([$"stop {TestMachine.ServiceName} within 00:01:00"], _machine.Operations);
    }

    // A token cancelled before the run, as by Ctrl+C before the first step, on a machine where every step would change
    // something.
    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    [InlineData("pair")]
    [InlineData("secret")]
    [InlineData("check")]
    [InlineData("metadata")]
    [InlineData("connector register")]
    public async Task A_cancelled_run_ends_with_Cancelled_before_its_first_step(string command)
    {
        _machine.Installed();
        _machine.HasService();
        _machine.Input = new FakeInput(_machine.Data, "a password\n");
        _machine.Connectors.Installed(ConnectorBitnessEnum.X64, "8.3.27.1859");
        using CancellationTokenSource cancelled = new();
        cancelled.Cancel();

        CommandAppResult result = await TestTimes.EndOf(_machine.Start(cancelled.Token, TestMachine.CommandLine(command)));

        Assert.Equal(ConsoleExitCodes.Cancelled, result.ExitCode);
        Assert.Contains("Cancelled (5): the command was cancelled", TestMachine.LinesOf(result));
        Assert.Empty(_machine.Operations);
        Assert.Empty(_machine.Routes);
    }
}
