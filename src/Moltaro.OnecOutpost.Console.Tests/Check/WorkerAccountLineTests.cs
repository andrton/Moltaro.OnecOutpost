using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line WorkerAccount of check, for each base whose policy names a
// worker account: its batch logon with the password of secrets.dat, and the rule of its token. A logon that fails is
// named by WorkerAccountReasons.CannotLogOn, the sentence that the agent's start gives too, and so is a token whose
// groups or privileges cannot be read; a token beyond an ordinary user's by NotOrdinary. The account is logged on once,
// never again after a failure, which counts toward a lockout; the probe and the connections of its base are not
// checked then, and those of a base without a worker account are.
public sealed class WorkerAccountLineTests : IDisposable
{
    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    // Two codes of the table, which WorkerAccountReasonsTests states whole, and one that it does not name.
    [Theory]
    [InlineData(1326)]
    [InlineData(1909)]
    [InlineData(1311)]
    public async Task A_worker_account_that_cannot_log_on_fails_the_line_with_the_reason_of_its_code_and_its_base_starts_no_worker(int error)
    {
        Machine.Workers.LogOnErrors[CheckedMachine.WorkerAccount] = error;

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        string reason = run.Failed(CheckLineEnum.WorkerAccount, "accounting", CheckedMachine.WorkerAccount);
        Assert.Contains(CheckedMachine.WorkerAccount, reason, StringComparison.Ordinal);
        Assert.EndsWith(WorkerAccountReasons.CannotLogOn("accounting", error), reason, StringComparison.Ordinal);
        AssertNoWorkerOfAccounting(run);
    }

    [Fact]
    public async Task A_worker_account_whose_token_is_beyond_an_ordinary_user_fails_the_line_and_its_base_starts_no_worker()
    {
        Machine.Workers.Administrators.Add(CheckedMachine.WorkerAccount);

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Equal(
            $"{CheckedMachine.WorkerAccount} {WorkerAccountReasons.NotOrdinary}",
            run.Failed(CheckLineEnum.WorkerAccount, "accounting", CheckedMachine.WorkerAccount));
        AssertNoWorkerOfAccounting(run);
    }

    // 1312, ERROR_NO_SUCH_LOGON_SESSION, for the groups of a token that cannot be read.
    [Fact]
    public async Task A_worker_account_whose_token_cannot_be_read_fails_the_line_with_the_reason_of_its_error()
    {
        Machine.Workers.TokenErrors[CheckedMachine.WorkerAccount] = 1312;

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.EndsWith(
            WorkerAccountReasons.CannotLogOn("accounting", 1312),
            run.Failed(CheckLineEnum.WorkerAccount, "accounting", CheckedMachine.WorkerAccount),
            StringComparison.Ordinal);
        AssertNoWorkerOfAccounting(run);
    }

    // The base's probe and connections are not checked, naming the worker account's line, nothing of the base starts,
    // and the account was logged on once; the base without a worker account is checked.
    private void AssertNoWorkerOfAccounting(CheckRun run)
    {
        run.AssertNotChecked(CheckLineEnum.WorkerStart, CheckedMachine.WorkerAccount, "accounting");
        run.AssertNotChecked(CheckLineEnum.Connection, CheckedMachine.WorkerAccount, "accounting", "outpost-read");
        run.AssertNotChecked(CheckLineEnum.Connection, CheckedMachine.WorkerAccount, "accounting", "outpost-write");
        run.Passed(CheckLineEnum.Connection, "warehouse", "outpost-read");
        Assert.DoesNotContain(Machine.Workers.Processes, process => process.Base == "accounting");
        Assert.DoesNotContain(Machine.Operations, line => line.StartsWith("probe ", StringComparison.Ordinal));
        Assert.Single(Machine.Operations, line => line.StartsWith("logon ", StringComparison.Ordinal));
    }
}
