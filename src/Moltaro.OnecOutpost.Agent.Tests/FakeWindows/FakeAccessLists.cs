using Moltaro.OnecOutpost.Agent.Access;

namespace Moltaro.OnecOutpost.Agent.Tests.FakeWindows;

// The access lists of a data folder, answered by path: every path admits nobody else until the test says that it admits
// some accounts, or that its list cannot be read. It records each path it is asked about. Not thread-safe.
internal sealed class FakeAccessLists : IAccessLists
{
    private readonly Dictionary<string, string[]> _others = new(StringComparer.Ordinal);

    private readonly HashSet<string> _unreadable = new(StringComparer.Ordinal);

    private readonly List<string> _asked = [];

    // The paths asked about, in the order of the questions.
    public IReadOnlyList<string> Asked => _asked;

    public IReadOnlyList<string> Others(string path)
    {
        _asked.Add(path);

        if (_unreadable.Contains(path))
        {
            throw new IOException($"The test made the access list of \"{path}\" unreadable.");
        }

        return _others.TryGetValue(path, out string[]? accounts) ? accounts : [];
    }

    // The program folder lets nobody else change it.
    public IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder)
    {
        return [];
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

    // Every path admits nobody else again, and every list can be read.
    public void Fix()
    {
        _others.Clear();
        _unreadable.Clear();
    }
}
