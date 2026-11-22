using Moltaro.OnecOutpost.Agent.Tests.FakeFiles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeWorkers;

// The launcher of the fake COM workers, in place of the COM worker of S9 behind its boundary (principle 8, the fake of
// the S6b brief). Each Start is a line "start {worker}" of the operation list of a data folder in memory, and so are
// the opens, the runs and the kills of its processes, so that a test asserts the order of the workers' operations
// among the writes of the audit log and the outbox. Opens decides what the open of each process gives, by its base,
// its mode and its number among the starts of that base and mode, from 1; Runs, the outcome function, decides what
// each run gives, by its command line. A test sets them before the pool asks. Start is called by one task at a time.
internal sealed class FakeWorkerLauncher : IWorkerLauncher
{
    private readonly MemoryDataFolder _data;

    private readonly List<FakeWorkerProcess> _processes = [];

    public FakeWorkerLauncher(MemoryDataFolder data)
    {
        _data = data;
    }

    public Func<string, OutpostModeEnum, int, FakeOpenEnum> Opens { get; set; } = (_, _, _) => FakeOpenEnum.Ready;

    public Func<WorkerCommandLine, FakeRun> Runs { get; set; } = _ => FakeRun.Gives(TestOutcomes.Succeeded());

    // Every process started, in the order of their starts.
    public IReadOnlyList<FakeWorkerProcess> Processes => _processes;

    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        int number = _processes.Count(process => process.Base == baseKey && process.Mode == mode) + 1;
        FakeWorkerProcess started = new(this, baseKey, mode, number);

        _processes.Add(started);
        Note($"start {started.Name}");

        return started;
    }

    // The number-th process started for a base and mode, from 1.
    public FakeWorkerProcess Process(string baseKey, OutpostModeEnum mode, int number = 1)
    {
        return _processes.Single(process => process.Base == baseKey && process.Mode == mode && process.Number == number);
    }

    internal void Note(string line)
    {
        _data.Note(line);
    }
}
