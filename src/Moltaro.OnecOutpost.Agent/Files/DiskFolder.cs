using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.Agent.Files;

// A folder of the data folder on the disk. It writes through FileStream with Flush(true), opens the files it writes
// sharing reading only, renames with MoveFileExW and MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH, and turns
// UnauthorizedAccessException into IOException. A same-volume rename rests on the journal of NTFS. A program that holds
// an audit file open without sharing writing fails the agent's next write, and the agent stops.
[SupportedOSPlatform("windows")]
internal sealed partial class DiskFolder : IAgentFolder
{
    // The flags of MoveFileExW: the new name replaces a file of that name, and the call returns only when the rename is
    // on the disk, so that an ended written after it never names an outcome file that a loss of power took back.
    private const uint MoveFileReplaceExisting = 0x1;

    private const uint MoveFileWriteThrough = 0x8;

    // The full path of the folder.
    private readonly string _path;

    public DiskFolder(string path)
    {
        _path = path;
    }

    public IReadOnlyList<string> Names()
    {
        try
        {
            return [.. new DirectoryInfo(_path).EnumerateFiles().Select(file => file.Name)];
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Refused(exception);
        }
    }

    public Stream OpenRead(string name)
    {
        try
        {
            return new FileStream(PathOf(name), FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Refused(exception);
        }
    }

    public void Append(string name, ReadOnlySpan<byte> bytes)
    {
        try
        {
            using FileStream stream = new(PathOf(name), FileMode.Append, FileAccess.Write, FileShare.Read);

            stream.Write(bytes);
            stream.Flush(flushToDisk: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Refused(exception);
        }
    }

    public void Cut(string name, long length)
    {
        try
        {
            using FileStream stream = new(PathOf(name), FileMode.Open, FileAccess.Write, FileShare.Read);

            stream.SetLength(length);
            stream.Flush(flushToDisk: true);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Refused(exception);
        }
    }

    public void Replace(string name, ReadOnlySpan<byte> bytes)
    {
        string temporary = PathOf($"{name}.tmp");

        try
        {
            using (FileStream stream = new(temporary, FileMode.Create, FileAccess.Write, FileShare.Read))
            {
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Refused(exception);
        }

        // The temporary file is on the disk before it takes the place of the file, so the file is always whole: the
        // old bytes or the new ones.
        if (!MoveFileEx(temporary, PathOf(name), MoveFileReplaceExisting | MoveFileWriteThrough))
        {
            int error = Marshal.GetLastPInvokeError();

            throw new IOException($"Windows did not rename {name}.tmp over {name}: {Marshal.GetPInvokeErrorMessage(error)}");
        }
    }

    public void Delete(string name)
    {
        try
        {
            File.Delete(PathOf(name));
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Refused(exception);
        }
    }

    public IDisposable Lock(string name)
    {
        try
        {
            return new FileStream(PathOf(name), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
        }
        catch (UnauthorizedAccessException exception)
        {
            throw Refused(exception);
        }
    }

    // Every failure of the folder is an IOException, as the boundary says.
    private static IOException Refused(UnauthorizedAccessException exception)
    {
        return new IOException(exception.Message, exception);
    }

    [LibraryImport("kernel32.dll", EntryPoint = "MoveFileExW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool MoveFileEx(string existingFileName, string newFileName, uint flags);

    private string PathOf(string name)
    {
        return Path.Combine(_path, name);
    }
}
