using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;

// The launcher of the fake COM workers, in place of the COM worker of S9 behind its boundary (principle 8, the fake of
// the S6b brief). Each Start is a line "start {worker}" of the operation list of a data folder in memory, and so are
// the opens, the runs and the kills of its processes, so that a test asserts the order of the workers' operations
// among the writes of the audit log and the outbox. Opens decides what the open of each process gives, by its base,
// its mode and its number among the starts of that base and mode, from 1; Runs, the outcome function, decides what
// each run gives, by its command line. A test sets them before the pool asks. Started, when a test sets it, runs once a
// process is started and listed, before Start returns it, so that a test acts at that moment. Start may run on several
// tasks at once, since every slot of the pool starts its workers on a task of its own (the S9b brief).
internal sealed class FakeWorkerLauncher : IWorkerLauncher
{
    private readonly MemoryDataFolder _data;

    // Guards the list. Nothing of a test runs under it.
    private readonly Lock _lock = new();

    private readonly List<FakeWorkerProcess> _processes = [];

    public FakeWorkerLauncher(MemoryDataFolder data)
    {
        _data = data;
    }

    public Func<string, OutpostModeEnum, int, FakeOpenEnum> Opens { get; set; } = (_, _, _) => FakeOpenEnum.Ready;

    public Func<WorkerCommandLine, FakeRun> Runs { get; set; } = _ => FakeRun.Gives(TestOutcomes.Succeeded());

    public Action<FakeWorkerProcess>? Started { get; set; }

    // Every process started, in the order of their starts, copied under the lock.
    public IReadOnlyList<FakeWorkerProcess> Processes
    {
        get
        {
            lock (_lock)
            {
                return [.. _processes];
            }
        }
    }

    // Logs nobody on and gives nothing back.
    public void LogOn(string baseKey, string account, string password)
    {
    }

    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        FakeWorkerProcess started;

        lock (_lock)
        {
            int number = _processes.Count(process => process.Base == baseKey && process.Mode == mode) + 1;
            started = new FakeWorkerProcess(this, baseKey, mode, number);
            _processes.Add(started);
        }

        Note($"start {started.Name}");
        Started?.Invoke(started);

        return started;
    }

    // The number-th process started for a base and mode, from 1.
    public FakeWorkerProcess Process(string baseKey, OutpostModeEnum mode, int number = 1)
    {
        return Processes.Single(process => process.Base == baseKey && process.Mode == mode && process.Number == number);
    }

    internal void Note(string line)
    {
        _data.Note(line);
    }
}
