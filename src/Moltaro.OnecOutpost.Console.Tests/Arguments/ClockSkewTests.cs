using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Server;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Arguments;

// --clock-skew of check (the S10b brief, "check", and CheckCommand.Settings): a time in the constant form [d.]hh:mm:ss
// of policy.json, from OutpostProtocol.MinClockSkew, 00:00:01, to MaxClockSkew, 01:00:00, which is how far check lets
// the server's clock be from this machine's. Absent, it is the server's default ClockSkew, since the console cannot read
// the server's own (principle 10). Anything else ends check with Usage before it runs, with the rule and without the
// value given.
public sealed class ClockSkewTests
{
    private readonly TestMachine _machine = new();

    // Accepted, it is the setting that check runs with, whatever check then finds on the machine.
    [Theory]
    [InlineData("00:00:01", 1)]
    [InlineData("01:00:00", 3600)]
    public async Task A_clock_skew_at_a_bound_of_its_range_is_accepted(string value, int seconds)
    {
        CommandAppResult result = await _machine.RunAsync("check", "--clock-skew", value);

        Assert.NotEqual(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Equal(TimeSpan.FromSeconds(seconds), Assert.IsType<CheckCommand.Settings>(result.Settings).ClockSkew);
    }

    [Fact]
    public async Task Without_a_clock_skew_check_allows_the_default_skew_of_the_server()
    {
        CommandAppResult result = await _machine.RunAsync("check");

        Assert.NotEqual(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Equal(new OutpostServerOptions().ClockSkew, Assert.IsType<CheckCommand.Settings>(result.Settings).ClockSkew);
    }

    // Below and beyond the range, and a time that is not in the constant form.
    [Theory]
    [InlineData("00:00:00")]
    [InlineData("01:00:01")]
    [InlineData("2:00")]
    public async Task A_clock_skew_out_of_its_range_or_form_ends_check_with_Usage_naming_the_rule_and_not_the_value(string value)
    {
        CommandAppResult result = await _machine.RunAsync("check", "--clock-skew", value);

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Equal(["Usage (2): " + CheckCommand.Settings.ClockSkewRule], TestMachine.LinesOf(result));
        Assert.Empty(_machine.Operations);
    }
}
