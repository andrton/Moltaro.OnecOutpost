using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// metadata ends only once each worker it started has exited: a session ends its worker by EndAsync within the
// read mode's runLimit, which closes its input and waits for its exit, terminating it at the deadline; Ctrl+C
// terminates at once and waits, without the run limit. So no worker of metadata runs once it has ended, and the program
// folder can be updated at once.
public sealed class MetadataWorkerEndTests : IDisposable
{
    private readonly MetadataMachine _metadata = new();

    private TestMachine Machine => _metadata.Machine;

    public void Dispose()
    {
        _metadata.Dispose();
    }

    // Documents loses the connection, so the walk opens a second session: each ends within the read mode's runLimit and
    // has exited when metadata ends.
    [Fact]
    public async Task Each_worker_of_metadata_has_exited_within_the_run_limit_of_the_read_mode_when_metadata_ends()
    {
        _metadata.Accounting.LosesConnection("Document.GoodsReceipt");

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 5, found: 6, failed: 1);
        Assert.Equal(2, Machine.Workers.Processes.Count);
        Assert.All(Machine.Workers.Processes, process =>
        {
            Assert.True(process.Exited, $"A worker of {process.Base} had not exited when metadata ended.");
            Assert.Equal(MetadataMachine.RunLimit, process.EndDeadline);
        });
    }

    // The worker goes on after its input closed: metadata waits the read mode's runLimit for it, then terminates it,
    // and only then ends.
    [Fact]
    public async Task A_worker_that_does_not_exit_at_the_end_of_its_input_is_terminated_at_the_run_limit_before_metadata_ends()
    {
        _metadata.SessionOf("accounting").LingerAtEnd();

        Task<CommandAppResult> started = _metadata.Start();
        await Machine.Data.WaitForAsync(change => change.Line == "end accounting outpost-read within 00:10:00");
        await TestTimes.WaitForTimerAsync(Machine.Clock, Machine.Clock.GetUtcNow() + MetadataMachine.RunLimit);
        Assert.False(started.IsCompleted, "metadata ended before its worker exited.");
        await TestTimes.PassExactlyAsync(Machine.Clock, MetadataMachine.RunLimit, "end of the worker");
        MetadataRun run = _metadata.Ended(await TestTimes.EndOf(started));

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
        FakeCheckProcess process = Assert.Single(Machine.Workers.Processes);
        Assert.True(process.Terminated);
        Assert.True(process.Exited);
        Assert.Contains("terminate accounting outpost-read", Machine.Operations);
    }

    // Ctrl+C while the census is read: the worker, which would not exit at the end of its input, is terminated at once,
    // and metadata ends without waiting for the run limit, which the clock never reaches.
    [Fact]
    public async Task Ctrl_C_terminates_the_worker_at_once_and_metadata_ends_once_it_has_exited()
    {
        _metadata.SessionOf("accounting").LingerAtEnd();
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _metadata.Accounting.Reading = read =>
        {
            if (read == "census")
            {
                cancellation.Cancel();
            }
        };
        DateTimeOffset before = Machine.Clock.GetUtcNow();

        MetadataRun run = _metadata.Ended(await TestTimes.EndOf(
            Machine.Start(cancellation.Token, "metadata", "accounting", _metadata.ReportOf("accounting"))));

        run.AssertEnded(ConsoleExitCodes.Cancelled, "Cancelled (5): the command was cancelled");
        FakeCheckProcess process = Assert.Single(Machine.Workers.Processes);
        Assert.True(process.Exited, "The worker had not exited when metadata ended.");
        Assert.Equal(before, Machine.Clock.GetUtcNow());
    }
}
