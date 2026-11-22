using Moltaro.OnecOutpost.Agent.Start;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeFiles;

// A data folder in memory: the data folder itself, audit\ and outcomes\, whose changes share one list, so that a test
// asserts the order of writes across the audit log and the outbox. The fake workers add their operations to the same
// list, so that a test asserts their order among the writes too. It outlives the runs of the agent that a test starts
// on it, as a disk does across restarts. The list is guarded by itself, since a fake worker adds to it from the task of
// its run while the folders change from the task of the loop.
internal sealed class MemoryDataFolder
{
    private readonly List<FolderChange> _changes = [];

    public MemoryDataFolder()
    {
        Root = new MemoryFolder(_changes, "");
        Audit = new MemoryFolder(_changes, "audit");
        Outcomes = new MemoryFolder(_changes, "outcomes");
    }

    public MemoryFolder Root { get; }

    public MemoryFolder Audit { get; }

    public MemoryFolder Outcomes { get; }

    public IReadOnlyList<FolderChange> Changes => _changes;

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
        lock (_changes)
        {
            _changes.Add(new FolderChange(line, []));
        }
    }
}
