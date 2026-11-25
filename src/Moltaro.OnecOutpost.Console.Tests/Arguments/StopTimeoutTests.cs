using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Arguments;

// --stop-timeout of install and uninstall: a time in the constant form
// [d.]hh:mm:ss of policy.json, from 00:00:01 to 01:00:00, 00:01:00 when absent, which is how long the command waits for
// the service to stop (principle 10). Anything else ends the command with Usage before it runs, with the rule and
// without the value given.
public sealed class StopTimeoutTests
{
    private readonly TestMachine _machine = new();

    // Accepted, it is the setting the command runs with, whatever the command then finds on the machine.
    [Theory]
    [InlineData("install", "00:00:01", 1)]
    [InlineData("uninstall", "01:00:00", 3600)]
    public async Task A_stop_timeout_at_a_bound_of_its_range_is_accepted(string command, string value, int seconds)
    {
        CommandAppResult result = await _machine.RunAsync(command, "--stop-timeout", value);

        Assert.NotEqual(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Equal(TimeSpan.FromSeconds(seconds), Assert.IsType<ServiceSettings>(result.Settings).StopTimeout);
    }

    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    public async Task Without_a_stop_timeout_a_command_waits_a_minute(string command)
    {
        CommandAppResult result = await _machine.RunAsync(command);

        Assert.NotEqual(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Equal(TimeSpan.FromMinutes(1), Assert.IsType<ServiceSettings>(result.Settings).StopTimeout);
    }

    // Below and beyond the range, and a time that is not in the constant form. Spectre's error for a value that the
    // converter refuses quotes the value and not the rule, so the console gives the converter's own, which names it.
    [Theory]
    [InlineData("install", "00:00:00")]
    [InlineData("uninstall", "01:00:01")]
    [InlineData("install", "1:00")]
    public async Task A_stop_timeout_out_of_its_range_or_form_ends_the_command_with_Usage_naming_the_rule_and_not_the_value(
        string command,
        string value)
    {
        CommandAppResult result = await _machine.RunAsync(command, "--stop-timeout", value);

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Equal(["Usage (2): " + ServiceSettings.StopTimeoutRule], TestMachine.LinesOf(result));
        Assert.Empty(_machine.Operations);
    }

    [Theory]
    [InlineData("install")]
    [InlineData("uninstall")]
    public async Task A_command_waits_for_the_service_to_stop_as_long_as_the_stop_timeout_given(string command)
    {
        _machine.HasService();

        await _machine.RunAsync(command, "--stop-timeout", "00:00:10");

        Assert.Equal($"stop {TestMachine.ServiceName} within 00:00:10", _machine.Operations[0]);
    }
}
