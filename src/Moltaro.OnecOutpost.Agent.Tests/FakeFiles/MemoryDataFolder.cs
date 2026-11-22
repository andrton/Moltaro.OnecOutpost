using Moltaro.OnecOutpost.Agent.Start;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeFiles;

// A data folder in memory: the data folder itself, audit\ and outcomes\, whose changes share one list, so that a test
// asserts the order of writes across the audit log and the outbox. The fake workers and the fake server add their
// operations to the same list, so that a test asserts their order among the writes too, and a test can wait for a
// change to be made. It outlives the runs of the agent that a test starts on it, as a disk does across restarts. The
// list is guarded by itself, since a fake worker adds to it from the task of its run while the folders change from the
// task of the loop.
internal sealed class MemoryDataFolder
{
    private readonly List<FolderChange> _changes = [];

    // The waits of WaitForAsync that no change has met yet, guarded by the lock of the list.
    private readonly List<(Func<FolderChange, bool> Match, TaskCompletionSource Met)> _waits = [];

    public MemoryDataFolder()
    {
        Root = new MemoryFolder(this, "");
        Audit = new MemoryFolder(this, "audit");
        Outcomes = new MemoryFolder(this, "outcomes");
    }

    public MemoryFolder Root { get; }

    public MemoryFolder Audit { get; }

    public MemoryFolder Outcomes { get; }

    // The changes made so far, copied under the lock of the list.
    public IReadOnlyList<FolderChange> Changes
    {
        get
        {
            lock (_changes)
            {
                return [.. _changes];
            }
        }
    }

    public AgentFolders Folders => new(Root, Audit, Outcomes);

    // The lines of the changes made since the list held `count` changes.
    public IReadOnlyList<string> LinesSince(int count)
    {
        lock (_changes)
        {
            return [.. _changes.Skip(count).Select(change => change.Line)];
        }
    }

    // Adds a line that changes no folder, such as an operation of a fake worker, after the changes made so far.
    public void Note(string line)
    {
        Add(new FolderChange(line, []));
    }

    // Completes once a change that the match accepts is in the list, at once if one is already: an event a test waits
    // for. It fails the test when none comes within the deadline of the tests.
    public Task WaitForAsync(Func<FolderChange, bool> match)
    {
        // Asynchronous continuations, so that the test never goes on inside the change of the code under test.
        TaskCompletionSource met = new(TaskCreationOptions.RunContinuationsAsynchronously);

        lock (_changes)
        {
            if (_changes.Any(match))
            {
                return Task.CompletedTask;
            }

            _waits.Add((match, met));
        }

        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return met.Task.WaitAsync(TestTimes.Deadline, cancellationToken);
    }

    // A change of a folder, or an operation, after the changes made so far.
    internal void Add(FolderChange change)
    {
        lock (_changes)
        {
            _changes.Add(change);

            for (int index = _waits.Count - 1; index >= 0; index--)
            {
                if (_waits[index].Match(change))
                {
                    _waits[index].Met.SetResult();
                    _waits.RemoveAt(index);
                }
            }
        }
    }
}
