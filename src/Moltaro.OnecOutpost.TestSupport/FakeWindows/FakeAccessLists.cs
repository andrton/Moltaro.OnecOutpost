using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.TestSupport.FakeWindows;

// The access lists of a data folder, answered by path: every path admits nobody else until the test says that it admits
// some accounts, or that its list cannot be read. It records each path it is asked about. The program folder lets
// nobody else change it until the test names entries through which others may, in the order Writers gives them, or
// says that its lists cannot be read; it records each folder it is asked about. When it is given a data folder in
// memory, each question is also a line "others {path}", the path quoted, or "writers {folder}" of its operation list,
// so that a test asserts the order of the judgments among the other operations. Not thread-safe.
internal sealed class FakeAccessLists : IAccessLists
{
    private readonly MemoryDataFolder? _notes;

    private readonly Dictionary<string, string[]> _others = new(StringComparer.Ordinal);

    private readonly HashSet<string> _unreadable = new(StringComparer.Ordinal);

    private readonly List<string> _asked = [];

    private readonly List<(string Path, IReadOnlyList<string> Accounts)> _writers = [];

    private readonly List<string> _programsAsked = [];

    private bool _programsUnreadable;

    public FakeAccessLists(MemoryDataFolder? notes = null)
    {
        _notes = notes;
    }

    // The paths asked about, in the order of the questions.
    public IReadOnlyList<string> Asked => _asked;

    // The folders whose writers were asked about, in the order of the questions.
    public IReadOnlyList<string> ProgramsAsked => _programsAsked;

    // The accounts it was told the path admits, as entries; no owner, and the path itself as the full path.
    public OtherAccounts Others(string path)
    {
        _asked.Add(path);
        _notes?.Note($"others \"{path}\"");

        if (_unreadable.Contains(path))
        {
            throw new IOException($"The test made the access list of \"{path}\" unreadable.");
        }

        return new OtherAccounts(path, null, _others.TryGetValue(path, out string[]? accounts) ? accounts : []);
    }

    public IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder)
    {
        _programsAsked.Add(folder);
        _notes?.Note($"writers {folder}");

        if (_programsUnreadable)
        {
            throw new IOException($"The test made the access lists of \"{folder}\" unreadable.");
        }

        return [.. _writers];
    }

    // The entry at the full path lets these accounts change the code of the program folder; Writers gives it after the
    // entries named before it.
    public void LetChange(string path, params string[] accounts)
    {
        _writers.Add((path, accounts));
    }

    public void MakeProgramsUnreadable()
    {
        _programsUnreadable = true;
    }

    // The entry at the path admits these accounts besides Administrators, SYSTEM and the agent's account.
    public void Admit(string path, params string[] accounts)
    {
        _others[path] = accounts;
    }

    public void MakeUnreadable(string path)
    {
        _unreadable.Add(path);
    }

    // Every path admits nobody else again, the program folder lets nobody else change it, and every list can be read.
    public void Fix()
    {
        _others.Clear();
        _unreadable.Clear();
        _writers.Clear();
        _programsUnreadable = false;
    }
}
