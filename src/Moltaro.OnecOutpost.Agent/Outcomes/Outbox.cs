using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Agent.Outcomes;

// The outbox in outcomes\: the outcome of each command not yet confirmed, in a file {id}.json. Not thread-safe: one
// task makes every change of the agent's files. Any IOException of the outbox throws AgentStopException, as a failed
// write of the audit log does.
internal sealed class Outbox
{
    private const string FileExtension = ".json";

    // A file written under this name and not yet renamed over its outcome file: torn, or left by a stop.
    private const string TemporaryExtension = ".tmp";

    private readonly IAgentFolder _folder;

    private readonly HashSet<Guid> _commands;

    private Outbox(IAgentFolder folder, HashSet<Guid> commands)
    {
        _folder = folder;
        _commands = commands;
    }

    // The commands whose outcome files the outbox holds.
    public IReadOnlyCollection<Guid> Commands => _commands;

    // Deletes every file whose name ends with .tmp, torn or left by a stop, and lists the files named {id}.json, the
    // identity in the form of §2 of docs/protocol.md. Any other name throws AgentStopException naming the file.
    public static Outbox Open(IAgentFolder outcomes)
    {
        HashSet<Guid> commands = [];

        foreach (string name in Names(outcomes))
        {
            if (name.EndsWith(TemporaryExtension, StringComparison.Ordinal))
            {
                Delete(outcomes, name);
            }
            else if (name.EndsWith(FileExtension, StringComparison.Ordinal)
                && OutpostForms.TryReadId(name.AsSpan(0, name.Length - FileExtension.Length), out Guid command))
            {
                commands.Add(command);
            }
            else
            {
                // Only the agent writes outcomes\, and only files of its form: anything else was planted, or is not
                // understood, and is no outcome to send.
                throw new AgentStopException($@"outcomes\{name} is not a file of the outbox; expected only files named by a command, such as outcomes\{Guid.Empty}.json");
            }
        }

        return new Outbox(outcomes, commands);
    }

    // The bytes of the outcome file of a command.
    public byte[] Read(Guid command)
    {
        try
        {
            return _folder.ReadAll(NameOf(command));
        }
        catch (IOException)
        {
            throw new AgentStopException($@"outcomes\{NameOf(command)} cannot be read");
        }
    }

    // Writes the outcome file of a command with Replace.
    public void Put(Guid command, ReadOnlySpan<byte> outcome)
    {
        try
        {
            _folder.Replace(NameOf(command), outcome);
        }
        catch (IOException)
        {
            throw new AgentStopException($@"outcomes\{NameOf(command)} cannot be written");
        }

        _commands.Add(command);
    }

    // Deletes the outcome file of a command.
    public void Remove(Guid command)
    {
        Delete(_folder, NameOf(command));

        _commands.Remove(command);
    }

    private static IReadOnlyList<string> Names(IAgentFolder outcomes)
    {
        try
        {
            return outcomes.Names();
        }
        catch (IOException)
        {
            throw new AgentStopException(@"outcomes\ cannot be listed");
        }
    }

    private static void Delete(IAgentFolder outcomes, string name)
    {
        try
        {
            outcomes.Delete(name);
        }
        catch (IOException)
        {
            throw new AgentStopException($@"outcomes\{name} cannot be removed");
        }
    }

    private static string NameOf(Guid command)
    {
        return OutpostForms.WriteId(command) + FileExtension;
    }
}
