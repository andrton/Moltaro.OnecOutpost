using Microsoft.AspNetCore.Http;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.Server.Signing;
using Moltaro.OnecOutpost.Server.Store;

namespace Moltaro.OnecOutpost.Server.Endpoints;

// The endpoint of `pair` (§6.1 of docs/protocol.md, concept "Pairing"). After the checks of §9 before the body, every
// failure gets the same bare 403, so that a refusal tells nothing of what failed: a body that breaks §2 or is not signed
// with the key it carries, a token that is unknown, used, expired or whose agent is not pending, a wrong proof, and a
// redemption that lost the race for the token. The 200 is signed, and repeats the key the store registered.
internal sealed class PairHandler
{
    private readonly OutpostStore _store;

    private readonly ServerSigningKey _signingKey;

    // The validated copy that AddOutpostServer keeps; nothing changes it.
    private readonly OutpostServerOptions _options;

    public PairHandler(OutpostStore store, ServerSigningKey signingKey, OutpostServerOptions options)
    {
        _store = store;
        _signingKey = signingKey;
        _options = options;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!AgentRequests.CheckHeaders(context, out Guid agentId, out byte[]? signature)
            || !AgentRequests.CheckLength(context, OutpostProtocol.PairBodyLimit))
        {
            return;
        }

        byte[]? body = await AgentRequests.ReadBodyAsync(context);

        if (body is null)
        {
            return;
        }

        OutpostPairRequest request;

        try
        {
            // The one request that is read before it is verified, since the key to verify it with is in its body (§3).
            request = OutpostCodec.ReadPairRequest(agentId, body, signature);
        }
        catch (OutpostProtocolException)
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status403Forbidden);

            return;
        }

        byte[]? secret = await _store.FindPairingSecretAsync(agentId, context.RequestAborted);

        // The secret never travels: only the proof of the key with it does (§3).
        if (secret is null || !OutpostPairingProof.Check(secret, agentId, request.Key, request.Proof))
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status403Forbidden);

            return;
        }

        // The token works once: of two pairings that passed the checks above, only one redeems it.
        if (!await _store.RedeemPairingTokenAsync(agentId, request.Key, context.RequestAborted))
        {
            AgentAnswers.WriteBare(context, StatusCodes.Status403Forbidden);

            return;
        }

        OutpostPairAnswer answer = new(
            _options.TimeProvider.GetUtcNow(),
            agentId,
            request.Key,
            _signingKey.PublicKey,
            _options.PollTimeout,
            _options.LeaseGrace);

        await AgentAnswers.WriteSignedAsync(
            context,
            _signingKey,
            OutpostRequestKindEnum.Pair,
            agentId,
            StatusCodes.Status200OK,
            body,
            OutpostCodec.Write(answer));
    }
}
