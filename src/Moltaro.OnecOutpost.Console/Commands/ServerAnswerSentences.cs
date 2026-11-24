using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Console.Commands;

// The console's own account of an answer to pair or to outcome that it does not accept (§6.1, §9 and §12 of
// docs/protocol.md), one table for pair and for the server's line of check. Nothing in it is taken from a body.
internal static class ServerAnswerSentences
{
    // HTTP {status}: first when a status came, then the sentence of the cause for the request, and for a verified
    // refusal that of its code.
    public static string Of(OutpostRequestKindEnum request, ServerAnswerCauseEnum cause, int? status, string? code)
    {
        bool pair = request == OutpostRequestKindEnum.Pair;

        string sentence = cause switch
        {
            ServerAnswerCauseEnum.Unreachable =>
                "no answer came: the server, or the proxy of the policy, could not be reached, or did not answer within the requestTimeout of the policy",
            ServerAnswerCauseEnum.ProtocolVersion =>
                "an unsigned answer in the form the server uses for a protocol version it does not speak",
            ServerAnswerCauseEnum.ServerKey => "the server's key does not match the fingerprint in the token",
            ServerAnswerCauseEnum.Signature => "the answer's signature does not verify",
            ServerAnswerCauseEnum.Refusal => RefusalOf(code),
            ServerAnswerCauseEnum.Bare when pair =>
                "a bare 403: the server answers so to any failure of pairing, and so can anything on the path",
            ServerAnswerCauseEnum.Bare => BareOf(status),
            _ when pair => "an answer that is not the signed answer of §6.1 of the protocol",
            _ => "an answer that is not a signed answer of §6.3 or §9 of the protocol",
        };

        return status is int came ? $"HTTP {came}: {sentence}" : sentence;
    }

    // A verified refusal of an outcome, by its code of §9.
    private static string RefusalOf(string? code)
    {
        return code switch
        {
            OutpostProtocolCodes.Signature =>
                "the server does not verify the agent's signature: secrets.dat and agent.json come from different pairings",
            OutpostProtocolCodes.Form => "the server refused the form of the request",
            _ => "the server refused even this small outcome for its size",
        };
    }

    // A bare 401, 403 or 413 to an outcome.
    private static string BareOf(int? status)
    {
        return status switch
        {
            401 => "the server knows no such agent, or it is still pending",
            403 => "the agent is revoked, or something on the path refused it",
            _ => "something on the path refused even this small body",
        };
    }
}
