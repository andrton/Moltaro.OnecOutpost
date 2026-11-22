using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// The COM workers of every base and mode of the policy, and the run limits of their commands (concept rules 8 and 9),
// with what turns an executor's object into the outcome sent. Not thread-safe, and no lock: the loop's task calls it, a
// run's task changes only its own worker, and Release reads what that left after the loop awaited the run. An open's
// task sets only its worker's opened flag, with Volatile.Write, and the loop reads it with Volatile.Read: the flag only
// goes from false to true, so a late read under-declares free and is safe.
internal sealed class WorkerPool : IAsyncDisposable
{
    // The executor is the path of OutpostExecutor.epf that every connection line names.
    public WorkerPool(AgentPolicy policy, AgentSecrets secrets, IWorkerLauncher launcher, string executor, TimeProvider clock)
    {
        throw new NotSupportedException();
    }

    // Starts the opens of the workers of every mode and returns at once. Each races Task.Delay(runLimit, clock): the
    // mode's run limit is its deadline (principle 10, concept §5), and a late worker is killed and, like one that gives
    // false, never free. So the loop starts at once, and a slow base holds up no other.
    public void Open(CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // Counts a mode's workers whose open ended with true and that hold no command. A command holds one from RunAsync,
    // called right after its received, to Release, called after End: it counts from its acceptance until its outcome is
    // recorded.
    public int Free(string baseKey, OutpostModeEnum mode)
    {
        throw new NotSupportedException();
    }

    // Takes a free worker of the command's base and mode before it returns; with none, it gives Failed base-unreachable
    // at once, in either mode, as the script never started (§8). A run races RunAsync with Task.Delay(runLimit, clock).
    // When the delay wins: Kill, a wait for the run's end, and run-limit, whatever the run then gives. Otherwise null
    // gives connection-lost; a size other than the length of the bytes, bytes that ReadOutcome refuses for the mode, and
    // a kind and code that IsExecutorOutcome refuses give result-type; anything else goes byte for byte. Each code is
    // Failed for a Read and Unknown for a Write, written as CommandRecords writes its own. DurationMs is the clock's
    // time from the call to the outcome. A worker is dropped after null, after a kill, and after it gave
    // base-unreachable or connection-lost, after which S9's worker ends; until S9b starts it again with a growing delay
    // (concept §5), its mode has one worker fewer. Cancellation kills the worker, waits for its run and throws,
    // recording nothing (rule 3 at the next start).
    public Task<WorkerRun> RunAsync(OutpostCommandMessage command, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }

    // Frees the worker that the command holds, after End recorded its outcome.
    public void Release(Guid command)
    {
        throw new NotSupportedException();
    }

    // Kills and disposes every worker.
    public ValueTask DisposeAsync()
    {
        throw new NotSupportedException();
    }
}
