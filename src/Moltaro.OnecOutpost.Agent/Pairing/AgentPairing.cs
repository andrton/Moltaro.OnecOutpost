using System.Net;
using System.Security.Cryptography;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Agent.Pairing;

// The console calls the agent's pairing client for the pair request (§6.1 of docs/protocol.md) and owns writing
// agent.json and secrets.dat.
internal static class AgentPairing
{
    // Makes a P-256 key, sends pair with the proof and reads the answer with ReadPairAnswer, waiting requestTimeout
    // over the answer and its body, read within 64 KiB. Gives Settings and the key in PKCS#8 for a verified 200;
    // otherwise the status when one came, and Unreachable without one, Bare for a bare 403, ProtocolVersion for the
    // unsigned body of §9, ServerKey and Signature for the codec's codes, Answer for anything else.
    public static async Task<AgentPairingResult> PairAsync(
        Uri server,
        OutpostPairingTokenText token,
        string agentVersion,
        HttpMessageHandler handler,
        TimeSpan requestTimeout,
        TimeProvider clock,
        CancellationToken cancellationToken)
    {
        using ECDsa key = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        byte[] publicKey = OutpostKeys.ExportPublicKey(key);

        // The proof binds the new key to the token's secret, which never travels (§3).
        byte[] proof = OutpostPairingProof.Compute(token.Secret, token.Agent, publicKey);
        byte[] body = OutpostCodec.Write(new OutpostPairRequest(agentVersion, publicKey, proof));
        byte[] signature = OutpostSignatures.SignRequest(key, OutpostRequestKindEnum.Pair, token.Agent, body);

        // The handler is the caller's: the console disposes it.
        using HttpMessageInvoker invoker = new(handler, disposeHandler: false);
        using HttpRequestMessage request = ServerRequests.Create(
            ServerRequests.PathOf(server, OutpostRequestKindEnum.Pair),
            OutpostRequestKindEnum.Pair,
            token.Agent,
            body,
            signature);
        using CancellationTokenSource timeout = new(requestTimeout, clock);
        using CancellationTokenSource wait = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);

        (HttpResponseMessage? response, int? status) = await ServerRequests.SendAsync(invoker, request, wait.Token, cancellationToken);

        if (response is null)
        {
            // A proxy that refused the tunnel gave a status; without one, nothing came.
            return Ended(status, status is null ? ServerAnswerCauseEnum.Unreachable : ServerAnswerCauseEnum.Answer);
        }

        ServerAnswer answer;

        using (response)
        {
            answer = await ServerRequests.ReadAsync(response, OutpostProtocol.AnswerLimit, wait.Token, cancellationToken);
        }

        // The agent knows no key that could verify a 403 before the pairing, so every 403 is bare, whatever its body.
        if (answer.Status == (int)HttpStatusCode.Forbidden)
        {
            return Ended(answer.Status, ServerAnswerCauseEnum.Bare);
        }

        if (answer.Body is not byte[] read)
        {
            // The status came, and no complete body within the limit and the wait: an answer all the same (§9).
            return Ended(answer.Status, ServerAnswerCauseEnum.Answer);
        }

        if (answer.IsUnsupportedVersion)
        {
            return Ended(answer.Status, ServerAnswerCauseEnum.ProtocolVersion);
        }

        if (answer.Status != (int)HttpStatusCode.OK || !answer.IsJson)
        {
            return Ended(answer.Status, ServerAnswerCauseEnum.Answer);
        }

        OutpostPairAnswer paired;

        try
        {
            // The fingerprint of the server key is checked against the token's before the signature is verified with
            // that key, and the answer must pair the identity and the key that were sent.
            paired = OutpostCodec.ReadPairAnswer(token.Fingerprint, token.Agent, publicKey, body, read, answer.Signature);
        }
        catch (OutpostProtocolException refusal)
        {
            ServerAnswerCauseEnum failure = refusal.Code switch
            {
                OutpostProtocolCodes.ServerKey => ServerAnswerCauseEnum.ServerKey,
                OutpostProtocolCodes.Signature => ServerAnswerCauseEnum.Signature,
                _ => ServerAnswerCauseEnum.Answer,
            };

            return Ended(answer.Status, failure);
        }

        return new AgentPairingResult
        {
            Settings = new AgentPairingSettings(server, paired.Agent, paired.ServerKey, paired.PollTimeout, paired.LeaseGrace),
            PrivateKey = key.ExportPkcs8PrivateKey(),
        };
    }

    private static AgentPairingResult Ended(int? status, ServerAnswerCauseEnum failure)
    {
        return new AgentPairingResult { Status = status, Failure = failure };
    }
}
