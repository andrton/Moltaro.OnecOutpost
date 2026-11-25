using System.ComponentModel;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Service;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Uninstall;

// uninstall [--stop-timeout <TIME>]: after the elevation, the stop of the service, then the
// two privileges taken from its account, then the service deleted. Both folders stay, with the audit log, and the
// worker accounts keep "Log on as a batch job". A service that does not exist, or does not stop in time, ends it before
// anything changes; a step that Windows refuses ends it with Failure and nothing after.
public sealed class UninstallTests
{
    private static readonly string Stop = $"stop {TestMachine.ServiceName} within 00:01:00";

    private static readonly string Revoke = $"revoke {TestMachine.AgentSid} SeAssignPrimaryTokenPrivilege, SeIncreaseQuotaPrivilege";

    private static readonly string Delete = $"delete {TestMachine.ServiceName}";

    private readonly TestMachine _machine = new();

    public UninstallTests()
    {
        _machine.HasService();
        _machine.Installed();
    }

    [Fact]
    public async Task Uninstall_stops_the_service_takes_the_two_rights_of_its_account_and_deletes_it_leaving_both_folders()
    {
        CommandAppResult result = await _machine.RunAsync("uninstall");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal([Stop, Revoke, Delete], _machine.Operations);

        string line = Assert.Single(TestMachine.LinesOf(result), line => line.StartsWith("Removed ", StringComparison.Ordinal));
        Assert.StartsWith($"Removed the service {TestMachine.ServiceName} and its account's two rights. Left ", line);
        Assert.Contains(TestMachine.ProgramFolder, line);
        Assert.Contains(TestMachine.DataFolder, line);
        Assert.EndsWith(", with the audit log; the worker accounts keep \"Log on as a batch job\".", line);
    }

    [Fact]
    public async Task Uninstall_without_the_service_ends_with_NotInstalled_and_changes_nothing()
    {
        _machine.Services.Exists = false;
        _machine.Services.StopResult = ServiceStopEnum.NotInstalled;

        CommandAppResult result = await _machine.RunAsync("uninstall");

        Assert.Equal(ConsoleExitCodes.NotInstalled, result.ExitCode);
        Assert.Contains("NotInstalled (6): ", result.Output);
        Assert.Contains("nothing was changed", result.Output);
        Assert.Equal([Stop], _machine.Operations);
    }

    [Fact]
    public async Task A_service_that_does_not_stop_within_the_stop_timeout_ends_uninstall_with_StopTimedOut()
    {
        _machine.Services.StopResult = ServiceStopEnum.TimedOut;

        CommandAppResult result = await _machine.RunAsync("uninstall");

        Assert.Equal(ConsoleExitCodes.StopTimedOut, result.ExitCode);
        Assert.Contains("StopTimedOut (7): ", result.Output);
        Assert.Equal([Stop], _machine.Operations);
    }

    [Fact]
    public async Task Rights_that_LSA_refuses_to_take_end_uninstall_with_Failure_and_the_service_stays()
    {
        _machine.Rights.RevokeFailure = new Win32Exception(5, "the fake LSA refused");

        CommandAppResult result = await _machine.RunAsync("uninstall");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): the step \"", result.Output);
        Assert.Contains("the fake LSA refused (Windows error 5)", result.Output);
        Assert.Equal([Stop, Revoke], _machine.Operations);
    }

    [Fact]
    public async Task A_service_that_Windows_does_not_delete_ends_uninstall_with_Failure()
    {
        _machine.Services.DeleteFailure = new Win32Exception(5, "the fake service controller refused");

        CommandAppResult result = await _machine.RunAsync("uninstall");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): the step \"", result.Output);
        Assert.Contains("the fake service controller refused (Windows error 5)", result.Output);
        Assert.Equal([Stop, Revoke, Delete], _machine.Operations);
    }
}
