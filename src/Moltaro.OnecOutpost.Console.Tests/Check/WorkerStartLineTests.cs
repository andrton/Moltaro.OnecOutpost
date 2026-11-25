using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line WorkerStart of check, for each worker account whose line passed: a start of the
// worker program of the connector's bitness under the account on its desktop, which opens no base, with the run limit of
// the base's write mode as its deadline. Its exit code 0 passes; every other end and every error of Windows is named,
// and the connections of the base are not checked then.
public sealed class WorkerStartLineTests : IDisposable
{
    // TestMachine's policy with run limits of its own, the read mode's longer than the write mode's.
    private const string Policy = """
        {
          "proxy": "http://proxy.example.com:3128",
          "requestTimeout": "00:00:20",
          "bases": [
            {
              "key": "accounting",
              "server": "srv-1c:1541",
              "database": "accounting",
              "read": { "account": "outpost-read", "runLimit": "00:05:00" },
              "write": { "account": "outpost-write", "runLimit": "00:03:00", "safeMode": false, "workerAccount": "outpost-worker" }
            },
            {
              "key": "warehouse",
              "file": "D:\\1C\\Warehouse",
              "read": { "account": "outpost-read" }
            }
          ]
        }
        """;

    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    // The workers of the account run the base's Writes, so the probe has the write mode's run limit.
    [Fact]
    public async Task The_probe_starts_the_worker_of_the_program_folder_for_the_base_within_the_run_limit_of_its_write_mode()
    {
        Machine.Data.Root.Seed(AgentFolders.PolicyName, Policy);

        CheckRun run = await _checked.RunAsync();

        run.Passed(CheckLineEnum.WorkerStart, "accounting", CheckedMachine.WorkerAccount);
        Assert.Single(Machine.Operations, $"probe accounting {CheckedMachine.WorkerAccount} {CheckedMachine.Program} within 00:03:00");
    }

    [Theory]
    [InlineData(0xC0000142u, "the worker failed to initialise on its desktop (STATUS_DLL_INIT_FAILED): the account or the desktop lacks access")]
    [InlineData(1u, "the worker refused its command line (exit code 1): the program folder holds another build")]
    [InlineData(0xE0434352u, "the worker ended with exit code 0xE0434352")]
    public async Task A_probe_that_ends_with_another_exit_code_than_0_fails_the_line_with_its_reason(uint exitCode, string reason)
    {
        Machine.Workers.ProbeExits[CheckedMachine.WorkerAccount] = exitCode;

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(reason, run.Failed(CheckLineEnum.WorkerStart, "accounting", CheckedMachine.WorkerAccount));
        AssertNoConnectionOfAccounting(run);
    }

    [Fact]
    public async Task A_probe_that_did_not_end_within_its_deadline_fails_the_line_naming_the_run_limit()
    {
        Machine.Workers.ProbeExits[CheckedMachine.WorkerAccount] = null;

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "the worker did not end within 00:10:00 and was ended",
            run.Failed(CheckLineEnum.WorkerStart, "accounting", CheckedMachine.WorkerAccount));
        AssertNoConnectionOfAccounting(run);
    }

    // The common launch errors have specific reasons. An unrecognized error carries the Windows message.
    [Theory]
    [InlineData(1314, "the console lacks \"Impersonate a client after authentication\"")]
    [InlineData(1058, "the Secondary Logon service is disabled; check does not start it")]
    [InlineData(2, "{program} is missing")]
    [InlineData(3, "{program} is missing")]
    [InlineData(5, "outpost-worker may not read or run {program}")]
    [InlineData(267, "outpost-worker may not read the folder of {program}, or it does not exist")]
    [InlineData(87, "the fake parameter is wrong (Windows error 87)")]
    public async Task A_probe_that_Windows_does_not_start_fails_the_line_with_the_reason_of_its_error(int error, string reason)
    {
        Machine.Workers.ProbeFailures[CheckedMachine.WorkerAccount] = new Win32Exception(error, "the fake parameter is wrong");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            reason.Replace("{program}", CheckedMachine.Program, StringComparison.Ordinal),
            run.Failed(CheckLineEnum.WorkerStart, "accounting", CheckedMachine.WorkerAccount));
        AssertNoConnectionOfAccounting(run);
    }

    [Fact]
    public async Task A_worker_account_that_gets_no_desktop_fails_the_line_with_the_message_of_Windows_and_starts_nothing()
    {
        Machine.Workers.DesktopFailures[CheckedMachine.WorkerAccount] = new Win32Exception(1307, "the fake station refused the desktop");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "outpost-worker cannot get a desktop: the fake station refused the desktop",
            run.Failed(CheckLineEnum.WorkerStart, "accounting", CheckedMachine.WorkerAccount));
        Assert.DoesNotContain(Machine.Operations, line => line.StartsWith("probe ", StringComparison.Ordinal));
        AssertNoConnectionOfAccounting(run);
    }

    // The connections of the base are not checked, naming the probe's line, and none starts; the base without a worker
    // account is checked.
    private void AssertNoConnectionOfAccounting(CheckRun run)
    {
        run.AssertNotChecked(CheckLineEnum.Connection, CheckedMachine.WorkerAccount, "accounting", "outpost-read");
        run.AssertNotChecked(CheckLineEnum.Connection, CheckedMachine.WorkerAccount, "accounting", "outpost-write");
        run.Passed(CheckLineEnum.Connection, "warehouse", "outpost-read");
        Assert.DoesNotContain(Machine.Workers.Processes, process => process.Base == "accounting");
    }
}
