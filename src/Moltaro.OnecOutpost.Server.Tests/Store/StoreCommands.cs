using System.Security.Cryptography;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server.Tests.Store;

// Commands as a call hands them to the store, for the tests of commands: the exact bytes of §7 of docs/protocol.md as
// the codec writes them, with random bytes in place of the server's signature, which the store keeps as it is given.
// A command is issued at the time given, with the defaults of the options unless a test names its own: a take timeout
// of 30 seconds, the clock skew of StoreAgent and an outcome size limit of 16 MiB. The bases that the polls of these
// tests declare give each mode a run limit of its own, so that a test sees which one a hand-out took.
internal static class StoreCommands
{
    public const string Script = "Result = 1;";

    public const int OutcomeLimit = 16 * 1024 * 1024;

    // The room of a poll answer for its commands when nothing else limits it (§10).
    public const long Room = OutpostProtocol.PollAnswerLimit;

    public static readonly TimeSpan TakeTimeout = TimeSpan.FromSeconds(30);

    public static readonly TimeSpan ReadRunLimit = TimeSpan.FromMinutes(5);

    public static readonly TimeSpan WriteRunLimit = TimeSpan.FromMinutes(7);

    // A command for the agent, its parameters given as JSON text, as the call that makes it writes and signs it.
    public static CommandRow Create(
        StoreAgent agent,
        string baseKey,
        OutpostModeEnum mode,
        string? callerKey,
        DateTimeOffset issued,
        string script = Script,
        string parameters = "{}",
        Guid? id = null,
        TimeSpan? takeTimeout = null,
        int outcomeLimit = OutcomeLimit)
    {
        Guid commandId = id ?? Guid.NewGuid();
        DateTimeOffset deadline = issued + (takeTimeout ?? TakeTimeout);

        using JsonDocument parsed = JsonDocument.Parse(parameters);

        OutpostCommandMessage message = new(
            OutpostProtocol.Version,
            commandId,
            agent.Id,
            baseKey,
            mode,
            callerKey,
            script,
            parsed.RootElement,
            issued,
            deadline,
            StoreAgent.ClockSkew,
            outcomeLimit);

        return new CommandRow
        {
            Id = commandId,
            AgentId = agent.Id,
            BaseKey = baseKey,
            Mode = mode,
            CallerKey = callerKey,
            Bytes = OutpostCodec.Write(message),
            Signature = RandomNumberGenerator.GetBytes(64),
            Deadline = deadline,
            Skew = StoreAgent.ClockSkew,
            OutcomeLimit = outcomeLimit,
            Phase = CommandPhaseEnum.Waiting,
        };
    }

    // A base that a poll declares, with the free workers given for Read and, unless null, for Write, each mode with
    // its run limit.
    public static OutpostPollBase Serving(string key, int readFree, int? writeFree)
    {
        OutpostPollMode? write = writeFree is int free ? new OutpostPollMode(free, WriteRunLimit) : null;

        return new OutpostPollBase(key, new OutpostPollMode(readFree, ReadRunLimit), write);
    }

    // Connects the agent with a poll that declares Reads on the base, creates a Read issued at the time given, and
    // hands it out with the agent's next poll.
    public static async Task<CommandRow> HandOutReadAsync(
        OutpostStore store,
        StoreAgent agent,
        string baseKey,
        DateTimeOffset issued,
        CancellationToken cancellationToken)
    {
        await agent.RecordAsync(agent.NextPoll([Serving(baseKey, 1, null)], []), cancellationToken);
        CommandRow read = Create(agent, baseKey, OutpostModeEnum.Read, null, issued);
        Assert.NotNull(await store.CreateCommandAsync(read, cancellationToken));

        PollTake take = await agent.PollAndTakeAsync([Serving(baseKey, 1, null)], [], cancellationToken);

        Assert.Equal<Guid>([read.Id], Identities(take));

        return read;
    }

    // The identities of the commands of a take, in the order of the answer, read from their bytes.
    public static IReadOnlyList<Guid> Identities(PollTake take)
    {
        List<Guid> identities = [];

        foreach (OutpostSignedCommand command in take.Commands)
        {
            using JsonDocument bytes = JsonDocument.Parse(command.Bytes);

            identities.Add(bytes.RootElement.GetProperty("id").GetGuid());
        }

        return identities;
    }
}
