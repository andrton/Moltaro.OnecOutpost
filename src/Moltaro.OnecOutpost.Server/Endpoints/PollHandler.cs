using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Hosting;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server.Signing;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server.Endpoints;

// The endpoint of `poll` (§6.2 of docs/protocol.md). After the checks of §9 before the body, it verifies and reads the
// poll, answering a signature that does not verify or a body that breaks its rules with the signed refusal of §9. The
// store then decides: an agent that is not paired gets the bare 403, an older poll its signed answer at once, and the
// newest poll is held. No commands exist before S5, so a hold ends only when its time is up, when the agent goes away
// or when the node stops.
//
// Nodes share nothing in memory (principle 6), so a held poll learns that a newer poll replaced it, or that its agent
// was revoked, only from the store, when its hold ends.
internal sealed class PollHandler
{
    private readonly OutpostStore _store;

    private readonly ServerSigningKey _signingKey;

    // The validated copy that AddOutpostServer keeps; nothing changes it.
    private readonly OutpostServerOptions _options;

    private readonly IHostApplicationLifetime _lifetime;

    public PollHandler(
        OutpostStore store,
        ServerSigningKey signingKey,
        OutpostServerOptions options,
        IHostApplicationLifetime lifetime)
    {
        _store = store;
        _signingKey = signingKey;
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

        PollRecord record = await _store.RecordPollAsync(
            agentId,
            poll,
            hold,
            _options.LeaseGrace,
            _options.ClockSkew,
            context.RequestAborted);

        PollState recorded = record.State;

        if (recorded.Revoked)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status403Forbidden);

            return;
        }

        // An older poll, an exact replay included, is answered at once and changed nothing (rule 10).
        if (recorded.Newest is not null)
        {
            await WriteAnswerAsync(context, agentId, body, recorded);

            return;
        }

        await HoldAsync(context, hold);

        // The agent went away: nothing is written, and the lease stays as the poll recorded it.
        if (context.RequestAborted.IsCancellationRequested)
        {
            return;
        }

        PollState state = await _store.ReadPollStateAsync(agentId, poll.Number, context.RequestAborted);

        if (state.Revoked)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status403Forbidden);

            return;
        }

        // A poll that a newer one replaced while it was held is answered as an older poll, with the newest number.
        await WriteAnswerAsync(context, agentId, body, state);
    }

    // Waits until the hold ends, the request is aborted or the node stops, whichever comes first. The timer of the hold
    // runs on the clock of the options and starts here, once the poll is recorded. The wait is on the linked token
    // alone, so it ends without an exception.
    private async Task HoldAsync(HttpContext context, TimeSpan hold)
    {
        using CancellationTokenSource holdEnds = new(hold, _options.TimeProvider);

        using CancellationTokenSource held = CancellationTokenSource.CreateLinkedTokenSource(
            holdEnds.Token,
            context.RequestAborted,
            _lifetime.ApplicationStopping);

        await Task.Delay(Timeout.InfiniteTimeSpan, held.Token).ConfigureAwait(ConfigureAwaitOptions.SuppressThrowing);
    }

    // The signed 200, with the node's time, the options that the agent keeps for its next poll and the lists of the
    // state. No commands before S5.
    private async Task WriteAnswerAsync(HttpContext context, Guid agentId, byte[] requestBody, PollState state)
    {
        OutpostPollAnswer answer = new(
            _options.TimeProvider.GetUtcNow(),
            state.Newest,
            _options.PollTimeout,
            _options.LeaseGrace,
            state.Held,
            state.Claimed,
            []);

        await AgentAnswers.WriteSignedAsync(
            context,
            _signingKey,
            OutpostRequestKindEnum.Poll,
            agentId,
            StatusCodes.Status200OK,
            requestBody,
            OutpostCodec.Write(answer));
    }
}
