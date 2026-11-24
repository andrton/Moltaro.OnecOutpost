using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Check;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line Connection of check (the S10b brief, "The connections"), for each base, read then write account: a worker in
// read mode that opens the base as the account and runs the script of its user, each within the mode's run limit, its
// worker killed at it. It passes when the session's user is the account of the policy, ignoring case, and fails with
// the reason of each end: a worker that Windows does not start, a session that 1C does not open with what the worker
// wrote to its standard error, a script that does not end well, another user, and a base without users, in which every
// connection has every right. The Subscriptions and ReadRights of a base run in the connection of its read account, so
// they are not checked when it fails. Each row names its mode, Connection {base} read|write {account}, so that a base
// whose two modes share one account has a row of each (the S10 brief, "After the lab run of deeb370", 3).
public sealed class ConnectionLineTests : IDisposable
{
    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(10);

    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    // 193, ERROR_BAD_EXE_FORMAT.
    [Fact]
    public async Task A_worker_that_Windows_does_not_start_fails_the_line_with_the_error_of_Windows()
    {
        Machine.Workers.StartFailures["warehouse"] = new Win32Exception(193, "the fake program is not a valid application");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            $"{CheckedMachine.Program} did not start: the fake program is not a valid application (Windows error 193)",
            run.Failed(CheckLineEnum.Connection, "warehouse", "outpost-read"));
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.ReadRights, "warehouse"), StringComparison.Ordinal);
    }

    // The COM worker writes why 1C opened no session to its standard error, withholding a text that holds the password.
    [Fact]
    public async Task A_session_that_1C_does_not_open_fails_the_line_with_the_diagnostics_of_the_worker()
    {
        const string Diagnostics = "No session opened: the user name or the password is wrong";
        Machine.Workers.Sessions[("accounting", "outpost-write")].RefuseOpen(Diagnostics);

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            $"1C opened no session: {Diagnostics}",
            run.Failed(CheckLineEnum.Connection, "accounting", "outpost-write"));
        Assert.Single(run.Rows, row => !row.Passed);
    }

    [Fact]
    public async Task A_session_that_1C_does_not_open_without_diagnostics_fails_the_line_saying_that_the_worker_gave_no_reason()
    {
        Machine.Workers.Sessions[("accounting", "outpost-write")].RefuseOpen("");

        CheckRun run = await _checked.RunAsync();

        Assert.Equal(
            "1C opened no session: the worker gave no reason",
            run.Failed(CheckLineEnum.Connection, "accounting", "outpost-write"));
    }

    // The first worker that check starts, so that no other wait of check is on the clock.
    [Fact]
    public async Task A_session_that_does_not_open_within_the_run_limit_of_the_mode_fails_the_line_and_its_worker_is_killed()
    {
        Machine.Workers.Sessions[("accounting", "outpost-read")].HangOpen();

        Task<CommandAppResult> started = _checked.Start();
        await Machine.Data.WaitForAsync(change => change.Line == "open accounting outpost-read");
        await TestTimes.PassExactlyAsync(Machine.Clock, RunLimit, "open of a worker");
        (await _checked.Stub.NextOutcomeAsync()).Confirm();
        CheckRun run = CheckedMachine.Ended(await TestTimes.EndOf(started));

        run.AssertCheckFailed();
        Assert.StartsWith(
            "1C opened no session within 00:10:00, the mode's runLimit",
            run.Failed(CheckLineEnum.Connection, "accounting", "outpost-read"),
            StringComparison.Ordinal);
        Assert.Contains("kill accounting outpost-read", Machine.Operations);
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.Subscriptions, "accounting"), StringComparison.Ordinal);
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.ReadRights, "accounting"), StringComparison.Ordinal);
        run.Passed(CheckLineEnum.Connection, "accounting", "outpost-write");
    }

    [Fact]
    public async Task A_script_of_the_check_that_fails_in_1C_fails_the_line_with_its_kind_its_code_and_the_text_of_1C()
    {
        Machine.Workers.Sessions[("warehouse", "outpost-read")].Fails(FakeCheckProcess.UserScript, "The fake session refused the call");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "the script of the check ended failed script-error: The fake session refused the call",
            run.Failed(CheckLineEnum.Connection, "warehouse", "outpost-read"));
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.ReadRights, "warehouse"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_script_of_the_check_that_does_not_end_within_the_run_limit_of_the_mode_fails_the_line_and_its_worker_is_killed()
    {
        Machine.Workers.Sessions[("accounting", "outpost-read")].Overruns(FakeCheckProcess.UserScript);

        Task<CommandAppResult> started = _checked.Start();
        await Machine.Data.WaitForAsync(change => change.Line == "run accounting outpost-read user");
        await TestTimes.PassExactlyAsync(Machine.Clock, RunLimit, "script of the check");
        (await _checked.Stub.NextOutcomeAsync()).Confirm();
        CheckRun run = CheckedMachine.Ended(await TestTimes.EndOf(started));

        run.AssertCheckFailed();
        Assert.Equal(
            "the script of the check did not end within 00:10:00",
            run.Failed(CheckLineEnum.Connection, "accounting", "outpost-read"));
        Assert.Contains("kill accounting outpost-read", Machine.Operations);
    }

    // A session of another user, such as an operating system authentication that 1C preferred, would judge the rights
    // of that user.
    [Fact]
    public async Task A_session_of_another_user_than_the_account_of_the_policy_fails_the_line_naming_both()
    {
        Machine.Workers.Sessions[("warehouse", "outpost-read")] = new FakeSession("Administrator", "The administrator");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "the session's user is Administrator, not outpost-read of the policy",
            run.Failed(CheckLineEnum.Connection, "warehouse", "outpost-read"));
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.ReadRights, "warehouse"), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_session_whose_user_differs_from_the_account_of_the_policy_in_case_alone_passes()
    {
        Machine.Workers.Sessions[("warehouse", "outpost-read")] = new FakeSession("Outpost-Read", "Outpost read");

        CheckRun run = await _checked.RunAsync();

        Assert.Equal("opened as Outpost-Read (Outpost read)", run.Passed(CheckLineEnum.Connection, "warehouse", "outpost-read"));
    }

    // A typical copy of the lab reads and writes under one 1C account; check opens a connection of each mode with it.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_base_whose_two_modes_share_one_account_has_a_row_of_each_mode_named_by_it()
    {
        Machine.Data.Root.Seed(
            AgentFolders.PolicyName,
            TestMachine.Policy.Replace("\"account\": \"outpost-write\"", "\"account\": \"outpost-read\"", StringComparison.Ordinal));

        CheckRun run = await _checked.RunAsync();

        run.AssertSucceeded();
        Assert.Equal(
            ["Connection accounting read outpost-read", "Connection accounting write outpost-read", "Connection warehouse read outpost-read"],
            run.RowsOf(CheckLineEnum.Connection).Select(row => row.Check));
        Assert.Equal("opened as outpost-read (Outpost read)", CheckTable.Row(run.Rows, "Connection", "accounting write outpost-read").Reason);
    }

    // 1C opens a base without users to anybody, with every right, whatever the account.
    [Fact]
    public async Task A_base_without_users_fails_the_line_since_every_connection_has_every_right()
    {
        Machine.Workers.Sessions[("warehouse", "outpost-read")] = new FakeSession("", "");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "the base has no users, so every connection has every right",
            run.Failed(CheckLineEnum.Connection, "warehouse", "outpost-read"));
        Assert.StartsWith("not checked: ", run.Failed(CheckLineEnum.ReadRights, "warehouse"), StringComparison.Ordinal);
    }
}
