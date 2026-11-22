using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server.Signing;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server.Endpoints;

// The endpoint of `poll` (§6.2 of docs/protocol.md). After the checks of §9 before the body, it verifies and reads the
// poll, answering a signature that does not verify or a body that breaks its rules with the signed refusal of §9. The
// store then decides: an agent that is not paired gets the bare 403, an older poll its signed answer at once, and the
// newest poll is held. While it is held it takes the commands that are due for its free modes, and answers at once
// with what it took; otherwise its hold ends when its time is up, when the agent goes away or when the node stops.
//
// Nodes share nothing in memory (principle 6). A held poll is woken through the signals, on the key of its agent, and
// then takes from the store, which alone tells it that a command is due, that a newer poll replaced it or that its
// agent was revoked. A wake-up that was lost costs it at most its hold, whose end reads the store too.
internal sealed class PollHandler
{
    private readonly OutpostStore _store;

    private readonly ServerSigningKey _signingKey;

    private readonly ISignals _signals;

    // The validated copy that AddOutpostServer keeps; nothing changes it.
    private readonly OutpostServerOptions _options;

    private readonly IHostApplicationLifetime _lifetime;

    public PollHandler(
        OutpostStore store,
        ServerSigningKey signingKey,
        ISignals signals,
        OutpostServerOptions options,
        IHostApplicationLifetime lifetime)
    {
        _store = store;
        _signingKey = signingKey;
        _signals = signals;
        _options = options;
        _lifetime = lifetime;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!AgentRequests.CheckHeaders(context, out Guid agentId, out byte[]? signature)
            || !AgentRequests.CheckLength(context, OutpostProtocol.PollBodyLimit))
        {
            return;
        }

        AgentRow? agent = await AgentRequests.FindPairedAgentAsync(context, _store, agentId);

        if (agent is null)
        {
            return;
        }

        byte[]? body = await AgentRequests.ReadBodyAsync(context);

        if (body is null)
        {
            return;
        }

        OutpostPollRequest poll;

        try
        {
            // A paired agent has the key that its pairing registered in the form of §2.
            using ECDsa agentKey = OutpostKeys.ImportPublicKey(agent.PublicKey);

            poll = OutpostCodec.ReadPollRequest(agentKey, agentId, body, signature);
        }
        catch (OutpostProtocolException exception)
        {
            await AgentAnswers.WriteRefusalAsync(
                context,
                _signingKey,
                OutpostRequestKindEnum.Poll,
                agentId,
                body,
                exception.Code,
                _options.TimeProvider.GetUtcNow());

            return;
        }

        // The hold never exceeds the wait of the agent, which its poll timeout bounds (§6.2).
        TimeSpan hold = poll.PollTimeout < _options.PollTimeout ? poll.PollTimeout : _options.PollTimeout;

        // A group of its own, so that a set of the key wakes this poll whatever else waits on it, an older poll of the
        // agent included. Subscribed before the poll is recorded, so that the set of every command created for the
        // agent once it is recorded reaches it; a set that came before the subscription was open on the host is
        // covered by the pass that follows its Joined.
        using ISignalSubscription wakes = _signals.Subscribe(_options.SignalKey(agentId), Guid.NewGuid().ToString("N"));

        PollRecord record = await _store.RecordPollAsync(
            agentId,
            poll,
            hold,
            _options.LeaseGrace,
            _options.ClockSkew,
            context.RequestAborted);

        // An agent that is not paired gets the bare 403, and an older poll, an exact replay included, is answered at
        // once and changed nothing (rule 10): neither made a command Expired.
        if (record.State.Revoked || record.State.Newest is not null)
        {
            await WriteAnswerAsync(context, agentId, body, record.State, []);

            return;
        }

        // The timer of the hold runs on the clock of the options from here, once the poll is recorded, so that the
        // hold ends within the lease that the poll recorded, however long the sets below take.
        using CancellationTokenSource holdEnds = new(hold, _options.TimeProvider);

        // The calls that wait for the commands that the list made Expired learn of it once it is committed. A set
        // after a commit is not cancelled, as OutpostServer says.
        foreach (Guid expired in record.Expired)
        {
            await _signals.SetAsync(_options.SignalKey(expired), CancellationToken.None);
        }

        // The room of the commands in the answer is the limit of §10 less the answer without them. The time has a
        // fixed length (§2), and the lists of the newest poll stay as recorded while it is the newest of a paired
        // agent: only a newer poll claims for the agent, and a release refuses a key it declares. So the answer
        // without commands keeps this length for the whole hold, and an answer with them is at most the limit.
        long room = OutpostProtocol.PollAnswerLimit - OutpostCodec.Write(Answer(record.State, [])).Length;

        PollTake? take = await HoldAsync(context, agentId, poll, room, wakes, holdEnds.Token);

        if (take is not null)
        {
            await WriteAnswerAsync(context, agentId, body, take.State, take.Commands);

            return;
        }

        // The agent went away: nothing is written, and the lease stays as the poll recorded it.
        if (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        // A poll that a newer one replaced, or whose agent was revoked, while it was held learns it here.
        PollState state = await _store.ReadPollStateAsync(agentId, poll.Number, context.RequestAborted);

        await WriteAnswerAsync(context, agentId, body, state, []);
    }

    // Passes and waits until a pass decides the answer or the hold ends: its time is up, the request is aborted or the
    // node stops, whichever comes first. A pass is a take. It decides the answer when it took anything, since a poll
    // that takes answers and so takes once, or when it found the agent revoked or the poll replaced. Null when the hold
    // ended without such a pass.
    private async Task<PollTake?> HoldAsync(
        HttpContext context,
        Guid agentId,
        OutpostPollRequest poll,
        long room,
        ISignalSubscription wakes,
        CancellationToken holdEnds)
    {
        using CancellationTokenSource held = CancellationTokenSource.CreateLinkedTokenSource(
            holdEnds,
            context.RequestAborted,
            _lifetime.ApplicationStopping);

        while (!held.IsCancellationRequested)
        {
            PollTake take = await _store.TakeCommandsAsync(agentId, poll, room, context.RequestAborted);

            if (take.State.Revoked || take.State.Newest is not null || take.Commands.Count > 0)
            {
                return take;
            }

            try
            {
                await wakes.WaitAsync(null, held.Token);
            }
            catch (OperationCanceledException) when (held.IsCancellationRequested)
            {
                // The hold has ended.
            }
        }

        return null;
    }

    // The answer of a state: the bare 403 for an agent that is not paired (§9), and otherwise the signed 200 with the
    // commands given.
    private async Task WriteAnswerAsync(
        HttpContext context,
        Guid agentId,
        byte[] requestBody,
        PollState state,
        IReadOnlyList<OutpostSignedCommand> commands)
    {
        if (state.Revoked)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status403Forbidden);

            return;
        }

        await AgentAnswers.WriteSignedAsync(
            context,
            _signingKey,
            OutpostRequestKindEnum.Poll,
            agentId,
            StatusCodes.Status200OK,
            requestBody,
            OutpostCodec.Write(Answer(state, commands)));
    }

    // The body of the 200 at the node's time, with the options that the agent keeps for its next poll and the lists of
    // the state.
    private OutpostPollAnswer Answer(PollState state, IReadOnlyList<OutpostSignedCommand> commands)
    {
        return new OutpostPollAnswer(
            _options.TimeProvider.GetUtcNow(),
            state.Newest,
            _options.PollTimeout,
            _options.LeaseGrace,
            state.Held,
            state.Claimed,
            commands);
    }
}
