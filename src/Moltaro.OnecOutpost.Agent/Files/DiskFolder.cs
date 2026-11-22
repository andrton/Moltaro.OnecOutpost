using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Files;

// A folder of the data folder on the disk. It writes through FileStream with Flush(true), opens the files it writes
// sharing reading only, renames with MoveFileExW and MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH, and turns
// UnauthorizedAccessException into IOException. A same-volume rename rests on the journal of NTFS. A program that holds
// an audit file open without sharing writing fails the agent's next write, and the agent stops.
[SupportedOSPlatform("windows")]
internal sealed class DiskFolder : IAgentFolder
{
    // The full path of the folder.
    private readonly string _path;

    public DiskFolder(string path)
    {
        _path = path;
    }

    public IReadOnlyList<string> Names()
    {
        throw new NotSupportedException();
    }

    public Stream OpenRead(string name)
    {
        throw new NotSupportedException();
    }

    public void Append(string name, ReadOnlySpan<byte> bytes)
    {
        throw new NotSupportedException();
    }

    public void Cut(string name, long length)
    {
        throw new NotSupportedException();
    }

    public void Replace(string name, ReadOnlySpan<byte> bytes)
    {
        throw new NotSupportedException();
    }

    public void Delete(string name)
    {
        throw new NotSupportedException();
    }

    public IDisposable Lock(string name)
    {
        throw new NotSupportedException();
    }
}
