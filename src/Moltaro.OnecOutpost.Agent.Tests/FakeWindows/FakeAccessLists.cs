using Moltaro.OnecOutpost.Agent.Access;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeWindows;

// The access lists of a data folder, answered by path: every path admits nobody else until the test says that it admits
// some accounts, or that its list cannot be read. It records each path it is asked about. The program folder lets
// nobody else change it until the test names entries through which others may, in the order Writers gives them, or
// says that its lists cannot be read; it records each folder it is asked about. Not thread-safe.
internal sealed class FakeAccessLists : IAccessLists
{
    private readonly Dictionary<string, string[]> _others = new(StringComparer.Ordinal);

    private readonly HashSet<string> _unreadable = new(StringComparer.Ordinal);

    private readonly List<string> _asked = [];

    private readonly List<(string Path, IReadOnlyList<string> Accounts)> _writers = [];

    private readonly List<string> _programsAsked = [];

    private bool _programsUnreadable;

    // The paths asked about, in the order of the questions.
    public IReadOnlyList<string> Asked => _asked;

    // The folders whose writers were asked about, in the order of the questions.
    public IReadOnlyList<string> ProgramsAsked => _programsAsked;

    public IReadOnlyList<string> Others(string path)
    {
        _asked.Add(path);

        if (_unreadable.Contains(path))
        {
            throw new IOException($"The test made the access list of \"{path}\" unreadable.");
        }

        return _others.TryGetValue(path, out string[]? accounts) ? accounts : [];
    }

    public IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder)
    {
        _programsAsked.Add(folder);

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
