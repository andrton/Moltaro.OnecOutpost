namespace Moltaro.OnecOutpost.Agent.Files;

// Reading a whole file of a folder, for the files that the agent reads at once: the outbox files and the files of the
// data folder itself. The audit log reads its files line by line instead.
internal static class AgentFolderExtensions
{
    // The bytes of a file; IOException when it cannot be opened or read.
    public static byte[] ReadAll(this IAgentFolder folder, string name)
    {
        using Stream stream = folder.OpenRead(name);
        using MemoryStream bytes = new();

        stream.CopyTo(bytes);

        return bytes.ToArray();
    }
}
