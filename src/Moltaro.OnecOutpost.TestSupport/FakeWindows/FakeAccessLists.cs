using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.TestSupport.FakeFiles;

namespace Moltaro.OnecOutpost.TestSupport.FakeWindows;

// The access lists of a data folder, answered by path: every path admits nobody else until the test says that it admits
// some accounts, that somebody else owns it, that its list cannot be read, or that it is gone, as a file that was listed
// and removed before its list was read. It records each path it is asked about. Each entry is named by its full path
// in the data folder that the test gives, or by the path itself without one. The program folder, and any other folder
// whose writers are asked about, such as the folder of a COM connector, lets nobody else change it until the test names
// entries through which others may, in the order Writers gives them: Writers gives a folder the entries named for its
// parents, for itself and for what it holds, so that one test names entries of two folders apart. It says that every
// such list cannot be read when the test says so, and records each folder it is asked about. When it is given a data
// folder in memory, each question is also a line "others {path}", the path quoted, or "writers {folder}" of its
// operation list, so that a test asserts the order of the judgments among the other operations. Not thread-safe.
internal sealed class FakeAccessLists : IAccessLists
{
    private readonly MemoryDataFolder? _notes;

    // The full path of the data folder, by which the entries are named; null to name each by its path.
    private readonly string? _dataFolder;

    private readonly Dictionary<string, string[]> _others = new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _owners = new(StringComparer.Ordinal);

    private readonly HashSet<string> _unreadable = new(StringComparer.Ordinal);

    private readonly HashSet<string> _gone = new(StringComparer.Ordinal);

    private readonly List<string> _asked = [];

    private readonly List<(string Path, IReadOnlyList<string> Accounts)> _writers = [];

    private readonly List<string> _programsAsked = [];

    private bool _programsUnreadable;

    public FakeAccessLists(MemoryDataFolder? notes = null, string? dataFolder = null)
    {
        _notes = notes;
        _dataFolder = dataFolder;
    }

    // The paths asked about, in the order of the questions.
    public IReadOnlyList<string> Asked => _asked;

    // The folders whose writers were asked about, in the order of the questions.
    public IReadOnlyList<string> ProgramsAsked => _programsAsked;

    // The owner the test named, apart, and the accounts it was told the path admits, as entries, by the full path.
    public OtherAccounts Others(string path)
    {
        _asked.Add(path);
        _notes?.Note($"others \"{path}\"");

        if (_gone.Contains(path))
        {
            throw new FileNotFoundException($"The test removed \"{path}\" after it was listed.");
        }

        if (_unreadable.Contains(path))
        {
            throw new IOException($"The test made the access list of \"{path}\" unreadable.");
        }

        return new OtherAccounts(
            FullPathOf(path),
            _owners.GetValueOrDefault(path),
            _others.TryGetValue(path, out string[]? accounts) ? accounts : []);
    }

    public IReadOnlyList<(string Path, IReadOnlyList<string> Accounts)> Writers(string folder)
    {
        _programsAsked.Add(folder);
        _notes?.Note($"writers {folder}");

        if (_programsUnreadable)
        {
            throw new IOException($"The test made the access lists of \"{folder}\" unreadable.");
        }

        return [.. _writers.Where(entry => OnTheWay(entry.Path, folder))];
    }

    // The full path by which Others names the entry at a path of the data folder.
    public string FullPathOf(string path)
    {
        if (_dataFolder is null)
        {
            return path;
        }

        return path.Length == 0 ? _dataFolder : Path.Combine(_dataFolder, path);
    }

    // The entry at the full path lets these accounts change the code of a folder on whose way it lies; Writers gives it
    // after the entries named before it.
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

    // The entry at the path is owned by this account, who can change its access list.
    public void Own(string path, string owner)
    {
        _owners[path] = owner;
    }

    public void MakeUnreadable(string path)
    {
        _unreadable.Add(path);
    }

    // The entry at the path was listed and is gone before its list is read: Others fails with FileNotFoundException.
    public void Remove(string path)
    {
        _gone.Add(path);
    }

    // Every path admits nobody else again and has no other owner, the program folder lets nobody else change it, and
    // every list can be read.
    public void Fix()
    {
        _others.Clear();
        _owners.Clear();
        _unreadable.Clear();
        _gone.Clear();
        _writers.Clear();
        _programsUnreadable = false;
    }

    // Whether the entry is a parent of the folder, the folder itself or inside it, whichever separator the paths use,
    // since the tests of the agent name Windows paths on every platform.
    private static bool OnTheWay(string path, string folder)
    {
        string entry = path.TrimEnd('\\', '/');
        string judged = folder.TrimEnd('\\', '/');

        return Within(judged, entry) || Within(entry, judged);
    }

    // Whether the path is the folder or lies inside it.
    private static bool Within(string path, string folder)
    {
        if (path == folder)
        {
            return true;
        }

        return path.Length > folder.Length
            && path.StartsWith(folder, StringComparison.Ordinal)
            && path[folder.Length] is '\\' or '/';
    }
}
