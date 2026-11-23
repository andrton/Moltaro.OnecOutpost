using System.Text;
using Moltaro.OnecOutpost.Agent.Files;

namespace Moltaro.OnecOutpost.TestSupport.FakeFiles;

// One folder of a data folder in memory, seeded by the test. Every change it makes is a line of the list of its data
// folder, so that a test asserts the order of writes across the audit log and the outbox; seeding and reading are not
// changes, and neither is a lock. Faults are injected by the test:
// - the next write, an Append or a Replace, keeps only its first bytes and then fails, which gives a torn entry or a
//   torn {name}.tmp and a failed write at once;
// - an OpenRead, a Cut, a Replace or a Delete of one name fails, until the test heals the folder;
// - a Lock fails while another holds it.
// Every failure is an IOException, as the boundary says. Names lists the files in the reverse order of their names, so
// that code which needs an order sorts them itself. Not thread-safe: the code under test makes one change at a time.
internal sealed class MemoryFolder : IAgentFolder
{
    // The data folder whose list of changes this folder adds to.
    private readonly MemoryDataFolder _data;

    // The path of the folder in the data folder: "" for the data folder itself, audit or outcomes.
    private readonly string _path;

    private readonly Dictionary<string, byte[]> _files = new(StringComparer.Ordinal);

    private readonly HashSet<string> _locked = new(StringComparer.Ordinal);

    // The operations, by name, that fail until Heal: "open", "cut", "replace" and "delete".
    private readonly HashSet<(string Operation, string Name)> _faults = [];

    // How many bytes the next Append or Replace keeps before it fails; null when it does not fail.
    private int? _nextWriteKeeps;

    public MemoryFolder(MemoryDataFolder data, string path)
    {
        _data = data;
        _path = path;
    }

    public IReadOnlyList<string> Names()
    {
        return [.. _files.Keys.OrderDescending(StringComparer.Ordinal)];
    }

    public Stream OpenRead(string name)
    {
        ThrowIfFaulted("open", name);

        return new MemoryStream(Existing(name), writable: false);
    }

    public void Append(string name, ReadOnlySpan<byte> bytes)
    {
        byte[] before = _files.GetValueOrDefault(name, []);
        byte[] written = TakeWritten(bytes, out bool fails);

        _files[name] = [.. before, .. written];
        Record($"append {PathOf(name)}", written);

        if (fails)
        {
            throw new IOException($"The test failed the write of {PathOf(name)}.");
        }
    }

    public void Cut(string name, long length)
    {
        ThrowIfFaulted("cut", name);

        byte[] before = Existing(name);
        byte[] after = new byte[length];
        before.AsSpan(0, (int)Math.Min(length, before.Length)).CopyTo(after);

        _files[name] = after;
        Record($"cut {PathOf(name)} to {length}", []);
    }

    public void Replace(string name, ReadOnlySpan<byte> bytes)
    {
        ThrowIfFaulted("replace", name);

        byte[] written = TakeWritten(bytes, out bool fails);
        Record($"replace {PathOf(name)}", written);

        if (fails)
        {
            // The rename never happened: the temporary file is torn and the file keeps what it held.
            _files[$"{name}.tmp"] = written;

            throw new IOException($"The test failed the write of {PathOf(name)}.tmp.");
        }

        _files.Remove($"{name}.tmp");
        _files[name] = written;
    }

    public void Delete(string name)
    {
        ThrowIfFaulted("delete", name);

        _files.Remove(name);
        Record($"delete {PathOf(name)}", []);
    }

    public IDisposable Lock(string name)
    {
        if (!_locked.Add(name))
        {
            throw new IOException($"{PathOf(name)} is held by another.");
        }

        _files.TryAdd(name, []);

        return new Held(this, name);
    }

    // Puts a file into the folder, as a stop or another program left it. Not a change.
    public void Seed(string name, byte[] bytes)
    {
        _files[name] = bytes;
    }

    public void Seed(string name, string text)
    {
        Seed(name, Encoding.UTF8.GetBytes(text));
    }

    // Takes a file out of the folder, as an administrator might. Not a change.
    public void Unseed(string name)
    {
        _files.Remove(name);
    }

    public bool Has(string name)
    {
        return _files.ContainsKey(name);
    }

    public byte[] Bytes(string name)
    {
        return Existing(name).ToArray();
    }

    public string Text(string name)
    {
        return Encoding.UTF8.GetString(Existing(name));
    }

    // The path of a file of this folder in the data folder, as a change names it.
    public string PathOf(string name)
    {
        return _path.Length == 0 ? name : $"{_path}\\{name}";
    }

    public void FailNextWrite(int bytesKept)
    {
        _nextWriteKeeps = bytesKept;
    }

    public void FailOpenRead(string name)
    {
        _faults.Add(("open", name));
    }

    public void FailCut(string name)
    {
        _faults.Add(("cut", name));
    }

    public void FailReplace(string name)
    {
        _faults.Add(("replace", name));
    }

    public void FailDelete(string name)
    {
        _faults.Add(("delete", name));
    }

    // Ends every injected fault; a lock that is held stays held.
    public void Heal()
    {
        _faults.Clear();
        _nextWriteKeeps = null;
    }

    private byte[] Existing(string name)
    {
        if (!_files.TryGetValue(name, out byte[]? bytes))
        {
            throw new FileNotFoundException($"{PathOf(name)} does not exist.");
        }

        return bytes;
    }

    // The bytes that a write puts into its file: all of them, or the first ones when the test failed this write.
    private byte[] TakeWritten(ReadOnlySpan<byte> bytes, out bool fails)
    {
        fails = _nextWriteKeeps is not null;

        if (_nextWriteKeeps is not int kept)
        {
            return bytes.ToArray();
        }

        _nextWriteKeeps = null;

        return bytes[..Math.Min(kept, bytes.Length)].ToArray();
    }

    private void ThrowIfFaulted(string operation, string name)
    {
        if (_faults.Contains((operation, name)))
        {
            throw new IOException($"The test failed the {operation} of {PathOf(name)}.");
        }
    }

    // The list is shared with the fake workers, which add to it from the tasks of their runs (MemoryDataFolder).
    private void Record(string line, byte[] bytes)
    {
        _data.Add(new FolderChange(line, bytes));
    }

    // A held lock, released once when it is disposed.
    private sealed class Held(MemoryFolder folder, string name) : IDisposable
    {
        private bool _released;

        public void Dispose()
        {
            if (_released)
            {
                return;
            }

            _released = true;
            folder._locked.Remove(name);
        }
    }
}
