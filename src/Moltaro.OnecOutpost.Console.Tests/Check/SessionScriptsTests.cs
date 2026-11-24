using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The scripts of one session of check as one ordered list (the S10 brief, "Internal of S10c", review 3's entry 2: the
// runner moved into WorkerSession and the scripts into SessionScript, check unchanged): the read account of accounting
// runs Connection, Subscriptions and ReadRights in that order, in one worker. When the first line fails, the session
// does not open, or a script ends the worker, every later script of the list is not checked and names that line, as
// "not checked: {line} failed", and runs not; a later script that fails in 1C fails its own line alone, and the next
// one still runs in the same worker.
public sealed class SessionScriptsTests : IDisposable
{
    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(10);

    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    // A session of another user fails the line Connection, whose user is the input of the rights and the subscriptions.
    [Fact]
    public async Task A_first_line_that_fails_leaves_every_later_script_of_its_session_not_checked_naming_that_line()
    {
        Machine.Workers.Sessions[("accounting", "outpost-read")] = new FakeSession("Administrator", "The administrator");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "not checked: Connection accounting outpost-read failed",
            run.Failed(CheckLineEnum.Subscriptions, "accounting"));
        Assert.Equal(
            "not checked: Connection accounting outpost-read failed",
            run.Failed(CheckLineEnum.ReadRights, "accounting"));
        Assert.Equal([FakeCheckProcess.UserScript], ScriptsOf("accounting", "outpost-read"));
    }

    [Fact]
    public async Task A_session_that_does_not_open_fails_its_first_line_and_leaves_every_later_one_not_checked_naming_it()
    {
        Machine.Workers.Sessions[("accounting", "outpost-read")].RefuseOpen("No session opened");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal("1C opened no session: No session opened", run.Failed(CheckLineEnum.Connection, "accounting", "outpost-read"));
        Assert.Equal(
            "not checked: Connection accounting outpost-read failed",
            run.Failed(CheckLineEnum.Subscriptions, "accounting"));
        Assert.Equal(
            "not checked: Connection accounting outpost-read failed",
            run.Failed(CheckLineEnum.ReadRights, "accounting"));
        Assert.Empty(ScriptsOf("accounting", "outpost-read"));
    }

    // A script error of 1C does not end the worker, so the rights are still read in it.
    [Fact]
    public async Task A_later_script_that_fails_in_1C_fails_its_own_line_alone_and_the_next_script_runs_in_the_same_worker()
    {
        Machine.Workers.Sessions[("accounting", "outpost-read")].Fails(FakeCheckProcess.SubscriptionsScript, "The fake session refused the call");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            "the script of the check ended failed script-error: The fake session refused the call",
            run.Failed(CheckLineEnum.Subscriptions, "accounting"));
        Assert.Equal("outpost-read holds no right to change data", run.Passed(CheckLineEnum.ReadRights, "accounting"));
        Assert.Single(run.Rows, row => !row.Passed);
        Assert.Equal(
            [FakeCheckProcess.UserScript, FakeCheckProcess.SubscriptionsScript, FakeCheckProcess.RightsScript],
            ScriptsOf("accounting", "outpost-read"));
    }

    // The run limit ends the worker while it reads the subscriptions, so the rights are not read in it.
    [Fact]
    public async Task A_script_that_ends_the_worker_leaves_every_later_script_of_its_session_not_checked_naming_its_line()
    {
        Machine.Workers.Sessions[("accounting", "outpost-read")].Overruns(FakeCheckProcess.SubscriptionsScript);

        Task<CommandAppResult> started = _checked.Start();
        await Machine.Data.WaitForAsync(change => change.Line == "run accounting outpost-read subscriptions");
        await TestTimes.PassExactlyAsync(Machine.Clock, RunLimit, "script of the subscriptions");
        (await _checked.Stub.NextOutcomeAsync()).Confirm();
        CheckRun run = CheckedMachine.Ended(await TestTimes.EndOf(started));

        run.AssertCheckFailed();
        Assert.Equal("the script of the check did not end within 00:10:00", run.Failed(CheckLineEnum.Subscriptions, "accounting"));
        Assert.Equal(
            "not checked: Subscriptions accounting failed",
            run.Failed(CheckLineEnum.ReadRights, "accounting"));
        Assert.Equal([FakeCheckProcess.UserScript, FakeCheckProcess.SubscriptionsScript], ScriptsOf("accounting", "outpost-read"));
        run.Passed(CheckLineEnum.Connection, "accounting", "outpost-write");
        run.Passed(CheckLineEnum.ReadRights, "warehouse");
    }

    // The kinds of the scripts that the workers of the account of the base ran, in their order.
    private IReadOnlyList<string> ScriptsOf(string baseKey, string user)
    {
        return
        [
            .. Machine.Workers.Processes
                .Where(process => process.Base == baseKey && process.Connection?.User == user)
                .SelectMany(process => process.Commands)
                .Select(command => FakeCheckProcess.KindOf(command.Script)),
        ];
    }
}
