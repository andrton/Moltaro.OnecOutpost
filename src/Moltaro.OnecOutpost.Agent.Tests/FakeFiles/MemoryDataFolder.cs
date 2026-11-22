using Moltaro.OnecOutpost.Agent.Start;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeFiles;

// A data folder in memory: the data folder itself, audit\ and outcomes\, whose changes share one list, so that a test
// asserts the order of writes across the audit log and the outbox. It outlives the runs of the agent that a test
// starts on it, as a disk does across restarts.
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
        return [.. _changes.Skip(count).Select(change => change.Line)];
    }
}
