using System.Security.Cryptography;
using Microsoft.AspNetCore.Http;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server.Signing;
using Moltaro.OnecOutpost.Server.Store;
using Moltaro.Signals;

namespace Moltaro.OnecOutpost.Server.Endpoints;

// The endpoint of `outcome` (§6.3 of docs/protocol.md). It makes the checks of §9 before the body, with the body limit
// of §10 that follows OutcomeSizeLimit, so that the web server's own default never refuses an outcome within it, and
// reads the body within that limit. Then, in the order of §6.3, each failure with its signed refusal of §9: the
// signature and the body by §2, the size of the outcome against the limit of its command, and the outcome object by §8
// for the mode of its command. For a command that the server does not know, the limit is OutcomeSizeLimit and either
// mode will do.
//
// The store keeps the first outcome of a command from the agent it was handed to, or one that replaces an outcome the
// server set (rule 11); every other is confirmed and changes nothing. The calls that wait for the command learn of a
// kept outcome through the signals, once it is committed.
internal sealed class OutcomeHandler
{
    private readonly OutpostStore _store;

    private readonly ServerSigningKey _signingKey;

    private readonly ISignals _signals;

    // The validated copy that AddOutpostServer keeps; nothing changes it.
    private readonly OutpostServerOptions _options;

    public OutcomeHandler(
        OutpostStore store,
        ServerSigningKey signingKey,
        ISignals signals,
        OutpostServerOptions options)
    {
        _store = store;
        _signingKey = signingKey;
        _signals = signals;
        _options = options;
    }

    public async Task HandleAsync(HttpContext context)
    {
        int bodyLimit = _options.OutcomeSizeLimit + OutpostProtocol.OutcomeEnvelopeAllowance;

        if (!AgentRequests.CheckHeaders(context, out Guid agentId, out byte[]? signature)
            || !AgentRequests.CheckLength(context, bodyLimit))
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

        OutpostOutcomeRequest request;

        try
        {
            // A paired agent has the key that its pairing registered in the form of §2.
            using ECDsa agentKey = OutpostKeys.ImportPublicKey(agent.PublicKey);

            request = OutpostCodec.ReadOutcomeRequest(agentKey, agentId, body, signature);
        }
        catch (OutpostProtocolException exception)
        {
            await WriteRefusalAsync(context, agentId, body, exception.Code);

            return;
        }

        CommandLimits? limits = await _store.FindCommandLimitsAsync(request.Command, context.RequestAborted);

        // The size of an outcome is the length of its object as it came (§8).
        if (request.Outcome.Length > (limits?.OutcomeLimit ?? _options.OutcomeSizeLimit))
        {
            await WriteRefusalAsync(context, agentId, body, OutpostProtocolCodes.ResultTooLarge);

            return;
        }

        OutpostOutcomeMessage message;

        try
        {
            message = OutpostCodec.ReadOutcome(limits?.Mode, request.Outcome.Span);
        }
        catch (OutpostProtocolException exception)
        {
            await WriteRefusalAsync(context, agentId, body, exception.Code);

            return;
        }

        bool kept = await _store.KeepOutcomeAsync(
            agentId,
            request.Command,
            message,
            request.Outcome,
            context.RequestAborted);

        if (kept)
        {
            await _signals.SetAsync(_options.SignalKey(request.Command), context.RequestAborted);
        }

        byte[] answer = OutpostCodec.Write(new OutpostOutcomeAnswer(_options.TimeProvider.GetUtcNow(), request.Command));

        await AgentAnswers.WriteSignedAsync(
            context,
            _signingKey,
            OutpostRequestKindEnum.Outcome,
            agentId,
            StatusCodes.Status200OK,
            body,
            answer);
    }

    private async Task WriteRefusalAsync(HttpContext context, Guid agentId, byte[] body, string code)
    {
        await AgentAnswers.WriteRefusalAsync(
            context,
            _signingKey,
            OutpostRequestKindEnum.Outcome,
            agentId,
            body,
            code,
            _options.TimeProvider.GetUtcNow());
    }
}
