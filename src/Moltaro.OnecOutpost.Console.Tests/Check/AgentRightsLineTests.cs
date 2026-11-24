using System.ComponentModel;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line AgentRights of check (the S10b brief, "check"): the rights that LSA shows the agent's account itself, by
// AgentSid, hold "Replace a process level token" and "Adjust memory quotas for a process", without which the service
// starts no worker under a worker account; a right that it lacks is named with the remedy, and rights that cannot be
// read with the error of Windows. The console starts its own workers without them, so no line needs this one.
public sealed class AgentRightsLineTests : IDisposable
{
    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Check_reads_the_rights_that_the_agent_account_holds_itself()
    {
        await _checked.RunAsync();

        Assert.Single(Machine.Operations, $"held {TestMachine.AgentSid}");
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(AccountRightNames.AssignPrimaryToken)]
    [InlineData(AccountRightNames.IncreaseQuota)]
    public async Task An_agent_account_that_lacks_one_of_its_two_privileges_fails_the_line_with_the_remedy_and_no_other_line(string held)
    {
        Machine.Rights.Rights[TestMachine.AgentSid] = [held, AccountRightNames.BatchLogon];

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.EndsWith($" {WorkerAccountReasons.AgentLacksPrivileges}", run.Failed(CheckLineEnum.AgentRights), StringComparison.Ordinal);
        Assert.Single(run.Rows, row => !row.Passed);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Rights_that_cannot_be_read_fail_the_line_with_the_error_of_Windows()
    {
        Machine.Rights.HeldFailure = new Win32Exception(5, "the fake LSA refused");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.EndsWith(
            " cannot be read: the fake LSA refused (Windows error 5)",
            run.Failed(CheckLineEnum.AgentRights),
            StringComparison.Ordinal);
    }
}
