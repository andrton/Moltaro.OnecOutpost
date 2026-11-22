namespace Moltaro.OnecOutpost.Agent.Files;

// One folder of the agent's data folder, by file name, without subfolders: the boundary through which the audit log
// and the outbox reach the disk, so that tests can replace it and see the order of writes, a torn entry, a failed write
// and a torn outbox file. Every failure is an IOException, and nothing retries.
internal interface IAgentFolder
{
    // The names of the files of the folder.
    IReadOnlyList<string> Names();

    // Reads a file from its start, sharing reading and writing.
    Stream OpenRead(string name);

    // Adds the bytes at the end of a file, creating the file when it is absent.
    void Append(string name, ReadOnlySpan<byte> bytes);

    // Sets the length of a file.
    void Cut(string name, long length);

    // Writes the bytes to {name}.tmp, flushes it to the disk and renames it over the file, the rename through to the
    // disk.
    void Replace(string name, ReadOnlySpan<byte> bytes);

    // Removes a file, absent or not. The removal need not reach the disk.
    void Delete(string name);

    // Opens a file, creating it, without sharing until the result is disposed.
    IDisposable Lock(string name);
}
