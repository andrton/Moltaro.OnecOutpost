using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// check ends only once each worker it started has exited: each session ends its worker by EndAsync within the
// runLimit of its mode, which closes its input and waits for its exit, terminating it at the deadline. The probe
// already waits so.
public sealed class WorkerEndTests : IDisposable
{
    // The policy of TestMachine with a runLimit of its own for each mode of accounting, so that each end names its
    // mode's.
    private const string Policy = """
        {
          "proxy": "http://proxy.example.com:3128",
          "requestTimeout": "00:00:20",
          "bases": [
            {
              "key": "accounting",
              "server": "srv-1c:1541",
              "database": "accounting",
              "read": { "account": "outpost-read", "runLimit": "00:03:00" },
              "write": { "account": "outpost-write", "runLimit": "00:04:00", "safeMode": false, "workerAccount": "outpost-worker" }
            },
            {
              "key": "warehouse",
              "file": "D:\\1C\\Warehouse",
              "read": { "account": "outpost-read" }
            }
          ]
        }
        """;

    private static readonly TimeSpan RunLimit = TimeSpan.FromMinutes(10);

    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    [Fact]
    public async Task Each_worker_of_check_has_exited_within_the_run_limit_of_its_mode_when_check_ends()
    {
        Machine.Data.Root.Seed(AgentFolders.PolicyName, Policy);

        CheckRun run = await _checked.RunAsync();

        run.AssertSucceeded();
        Assert.Equal(
            [
                ("accounting", "outpost-read", TimeSpan.FromMinutes(3)),
                ("accounting", "outpost-write", TimeSpan.FromMinutes(4)),
                ("warehouse", "outpost-read", RunLimit),
            ],
            Machine.Workers.Processes.Select(process => (process.Base, process.Connection!.User, process.EndDeadline ?? TimeSpan.Zero)));
        Assert.All(Machine.Workers.Processes, process => Assert.True(process.Exited, $"A worker of {process.Base} had not exited when check ended."));
    }

    // The worker of warehouse, the last that check starts, goes on after its input closed: check waits the runLimit of
    // its mode for it, terminates it, and only then reaches the server.
    [Fact]
    public async Task A_worker_that_does_not_exit_at_the_end_of_its_input_is_terminated_at_the_run_limit_before_check_goes_on()
    {
        Machine.Workers.Sessions[("warehouse", "outpost-read")].LingerAtEnd();

        Task<CommandAppResult> started = _checked.Start();
        await Machine.Data.WaitForAsync(change => change.Line == "end warehouse outpost-read within 00:10:00");
        await TestTimes.PassExactlyAsync(Machine.Clock, RunLimit, "end of the worker");
        (await _checked.Stub.NextOutcomeAsync()).Confirm();
        CheckRun run = CheckedMachine.Ended(await TestTimes.EndOf(started));

        run.AssertSucceeded();
        FakeCheckProcess warehouse = Assert.Single(Machine.Workers.Processes, process => process.Base == "warehouse");
        Assert.True(warehouse.Terminated);
        Assert.True(warehouse.Exited);
        Assert.True(
            Machine.Operations.ToList().IndexOf("terminate warehouse outpost-read") < Machine.Operations.ToList().IndexOf("request outcome"),
            "check reached the server before its last worker exited.");
    }
}
