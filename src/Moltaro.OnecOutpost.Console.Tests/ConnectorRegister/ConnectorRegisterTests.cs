using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.ConnectorRegister;

// connector register (the S10 brief, "connector register", and the owner's answer after L1): after the elevation, one
// line per finding of the finder, 64 bits first. A bitness with a connector registered is said to be; one without it
// and with exactly one copy installed gets that copy registered by the regsvr32 of its bitness, and the finder is read
// again; with several copies none is registered, since the connector has to match the version of the 1C cluster, and
// each is named with the command that registers it; with none, so it says. The exit is RegistrationFailed when a run of
// regsvr32 failed or HKLM shows nothing after it, else ConnectorNotRegistered when no bitness ends with a connector
// registered, else Success.
public sealed class ConnectorRegisterTests
{
    private readonly TestMachine _machine = new();

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_connector_registered_for_each_bitness_is_said_to_be_and_nothing_is_run()
    {
        ConnectorFile x64 = _machine.Connectors.Registered(ConnectorBitnessEnum.X64, "8.3.27.1859");
        ConnectorFile x86 = _machine.Connectors.Registered(ConnectorBitnessEnum.X86, "8.3.27.1859");

        CommandAppResult result = await _machine.RunAsync("connector", "register");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal(["find"], _machine.Operations);
        IReadOnlyList<string> lines = TestMachine.LinesOf(result);
        Assert.Contains($"64-bit: {x64.Path}, 8.3.27.1859, is registered.", lines);
        Assert.Contains($"32-bit: {x86.Path}, 8.3.27.1859, is registered.", lines);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_one_copy_installed_for_a_bitness_without_a_registration_is_registered_by_regsvr32_and_found_again()
    {
        ConnectorFile x64 = _machine.Connectors.Installed(ConnectorBitnessEnum.X64, "8.3.27.1859");

        CommandAppResult result = await _machine.RunAsync("connector", "register");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal(["find", $"regsvr32 X64 {x64.Path}", "find"], _machine.Operations);
        IReadOnlyList<string> lines = TestMachine.LinesOf(result);
        Assert.Contains($"64-bit: registered {x64.Path}, 8.3.27.1859.", lines);
        Assert.Contains(lines, line =>
            line.StartsWith("32-bit: ", StringComparison.Ordinal) && line.Contains("none is registered or installed", StringComparison.Ordinal));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Several_copies_of_a_bitness_are_named_with_the_command_that_registers_each_and_none_is_registered()
    {
        ConnectorFile older = _machine.Connectors.Installed(ConnectorBitnessEnum.X64, "8.3.24.1691");
        ConnectorFile newer = _machine.Connectors.Installed(ConnectorBitnessEnum.X64, "8.3.27.1859");

        CommandAppResult result = await _machine.RunAsync("connector", "register");

        Assert.Equal(ConsoleExitCodes.ConnectorNotRegistered, result.ExitCode);
        Assert.Equal(["find"], _machine.Operations);
        IReadOnlyList<string> lines = TestMachine.LinesOf(result);
        Assert.Contains(lines, line =>
            line.StartsWith("64-bit: ", StringComparison.Ordinal)
            && line.Contains(
                "none is registered, and the connector has to match the version of the 1C cluster, so register that one:",
                StringComparison.Ordinal));
        Assert.Contains($"8.3.24.1691: {_machine.Registration.CommandOf(ConnectorBitnessEnum.X64, older.Path)}", lines);
        Assert.Contains($"8.3.27.1859: {_machine.Registration.CommandOf(ConnectorBitnessEnum.X64, newer.Path)}", lines);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_machine_without_a_connector_ends_with_ConnectorNotRegistered()
    {
        CommandAppResult result = await _machine.RunAsync("connector", "register");

        Assert.Equal(ConsoleExitCodes.ConnectorNotRegistered, result.ExitCode);
        Assert.Equal(["find"], _machine.Operations);
        IReadOnlyList<string> lines = TestMachine.LinesOf(result);

        foreach (string bits in new[] { "64-bit: ", "32-bit: " })
        {
            Assert.Contains(lines, line =>
                line.StartsWith(bits, StringComparison.Ordinal) && line.Contains("none is registered or installed", StringComparison.Ordinal));
        }
    }

    // The exit codes of regsvr32 /s, by their kinds, said as the brief says the end of a run with 0 that registers nothing.
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(1, "arguments")]
    [InlineData(2, "OleInitialize")]
    [InlineData(3, "loading the DLL")]
    [InlineData(4, "no DllRegisterServer")]
    [InlineData(5, "DllRegisterServer failed")]
    public async Task A_run_of_regsvr32_that_fails_ends_with_RegistrationFailed_and_its_code_and_kind(int code, string kind)
    {
        ConnectorFile x64 = _machine.Connectors.Installed(ConnectorBitnessEnum.X64, "8.3.27.1859");
        _machine.Registration.ExitCode = code;

        CommandAppResult result = await _machine.RunAsync("connector", "register");

        Assert.Equal(ConsoleExitCodes.RegistrationFailed, result.ExitCode);
        Assert.Contains("RegistrationFailed (18): ", result.Output);
        Assert.Contains($"regsvr32 ended with {code}", result.Output);
        Assert.Contains(kind, result.Output);
        Assert.Equal(["find", $"regsvr32 X64 {x64.Path}"], _machine.Operations.Take(2));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_run_of_regsvr32_that_ends_with_0_without_a_registration_in_HKLM_ends_with_RegistrationFailed()
    {
        _machine.Connectors.Installed(ConnectorBitnessEnum.X64, "8.3.27.1859");
        _machine.Registration.Shows = false;

        CommandAppResult result = await _machine.RunAsync("connector", "register");

        Assert.Equal(ConsoleExitCodes.RegistrationFailed, result.ExitCode);
        Assert.Contains("RegistrationFailed (18): ", result.Output);
        Assert.Contains("regsvr32 ended with 0, yet HKLM shows no registration", result.Output);
    }

    // A failed run decides the exit, though the other bitness has its connector registered.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_failed_run_of_regsvr32_ends_with_RegistrationFailed_whatever_the_other_bitness_has()
    {
        _machine.Connectors.Installed(ConnectorBitnessEnum.X64, "8.3.27.1859");
        ConnectorFile x86 = _machine.Connectors.Registered(ConnectorBitnessEnum.X86, "8.3.27.1859");
        _machine.Registration.ExitCode = 3;

        CommandAppResult result = await _machine.RunAsync("connector", "register");

        Assert.Equal(ConsoleExitCodes.RegistrationFailed, result.ExitCode);
        Assert.Contains($"32-bit: {x86.Path}, 8.3.27.1859, is registered.", TestMachine.LinesOf(result));
    }
}
