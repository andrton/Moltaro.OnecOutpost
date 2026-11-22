using Moltaro.OnecOutpost.Agent.Files;

namespace Moltaro.OnecOutpost.Agent.Outcomes;

// The outbox in outcomes\: the outcome of each command not yet confirmed, in a file {id}.json. Not thread-safe: one
// task makes every change of the agent's files. Any IOException of the outbox throws AgentStopException, as a failed
// write of the audit log does.
internal sealed class Outbox
{
    // Deletes every file whose name ends with .tmp, torn or left by a stop, and lists the files named {id}.json, the
    // identity in the form of §2 of docs/protocol.md. Any other name throws AgentStopException naming the file.
    public static Outbox Open(IAgentFolder outcomes)
    {
        throw new NotSupportedException();
    }

    // The commands whose outcome files the outbox holds.
    public IReadOnlyCollection<Guid> Commands => throw new NotSupportedException();

    // The bytes of the outcome file of a command.
    public byte[] Read(Guid command)
    {
        throw new NotSupportedException();
    }

    // Writes the outcome file of a command with Replace.
    public void Put(Guid command, ReadOnlySpan<byte> outcome)
    {
        throw new NotSupportedException();
    }

    // Deletes the outcome file of a command.
    public void Remove(Guid command)
    {
        throw new NotSupportedException();
    }
}
