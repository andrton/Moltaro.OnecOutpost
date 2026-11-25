using Moltaro.OnecOutpost.Agent;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.TestSupport.FakeWorkers;

// The launcher of the fake COM workers, in place of the COM worker of S9 behind its boundary (principle 8, the fake of
// the S6b brief). Each Start is a line "start {worker}" of the operation list of a data folder in memory, and so are
// the opens, the runs and the kills of its processes, so that a test asserts the order of the workers' operations
// among the writes of the audit log and the outbox. Opens decides what the open of each process gives, by its base,
// its mode and its number among the starts of that base and mode, from 1; Runs, the outcome function, decides what
// each run gives, by its command line. A test sets them before the pool asks. Started, when a test sets it, runs once a
// process is started and listed, before Start returns it, so that a test acts at that moment. Start may run on several
// tasks at once, since every slot of the pool starts its workers on a task of its own (the S9b brief).
//
// LogOn stands for the logon of a worker account (the S9c brief): it keeps the base, the account and the password it
// was given, and is a line "logon {base} {account}", never with the password. It refuses the logon of a base that the
// test named with RefuseLogOn, with an AgentStopException of the test's reason, as the real launcher refuses one; any
// other base is logged on, and every process started for it afterwards runs under its account, Reads included, as the
// real launcher starts the workers of a logged-on base under its token. A process of a base that was not logged on
// runs under the agent's own account.
internal sealed class FakeWorkerLauncher : IWorkerLauncher
{
    private readonly MemoryDataFolder _data;

    // Guards the lists and the maps. Nothing of a test runs under it.
    private readonly Lock _lock = new();

    private readonly List<FakeWorkerProcess> _processes = [];

    private readonly List<(string Base, string Account, string Password)> _logOns = [];

    // The account of each base logged on.
    private readonly Dictionary<string, string> _accounts = new(StringComparer.Ordinal);

    // The reason with which the logon of a base is refused, by base key.
    private readonly Dictionary<string, string> _refused = new(StringComparer.Ordinal);

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

    // Every logon asked for, in the order of the calls, refused ones included, copied under the lock.
    public IReadOnlyList<(string Base, string Account, string Password)> LogOns
    {
        get
        {
            lock (_lock)
            {
                return [.. _logOns];
            }
        }
    }

    // The logon of the base is refused with an AgentStopException of this reason.
    public void RefuseLogOn(string baseKey, string reason)
    {
        lock (_lock)
        {
            _refused[baseKey] = reason;
        }
    }

    public void LogOn(string baseKey, string account, string password)
    {
        string? refused;

        lock (_lock)
        {
            _logOns.Add((baseKey, account, password));

            if (!_refused.TryGetValue(baseKey, out refused))
            {
                _accounts[baseKey] = account;
            }
        }

        Note($"logon {baseKey} {account}");

        if (refused is not null)
        {
            throw new AgentStopException(refused);
        }
    }

    public IWorkerProcess Start(string baseKey, OutpostModeEnum mode)
    {
        FakeWorkerProcess started;

        lock (_lock)
        {
            int number = _processes.Count(process => process.Base == baseKey && process.Mode == mode) + 1;
            started = new FakeWorkerProcess(this, baseKey, mode, number, _accounts.GetValueOrDefault(baseKey));
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
