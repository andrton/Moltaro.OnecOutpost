using System.Security.Cryptography;

namespace Moltaro.OnecOutpost.Protocol.Signing;

// The signatures of §2 and §3 of docs/protocol.md: ECDSA P-256 with SHA-256 over a signing input of §3, as the 64
// bytes r ‖ s (IEEE P1363). The signing inputs of §3 are built only here, for signing and for verifying alike. The
// readers of the codec call the Verify methods before they parse, and nothing else parses signed bytes, so no caller
// parses unverified bytes.
//
// A Sign method returns the 64 bytes of the signature. A Verify method returns false, without an exception, for a
// signature that does not verify, one of another length included. `agent` is the identity of the `Outpost-Agent`
// header of the request, and `status` the HTTP status of the answer.
internal static class OutpostSignatures
{
    // Signs a request with the agent's key, over `outpost/1 request {kind} {agent} {bodyHash}` (§3).
    public static byte[] SignRequest(ECDsa agentKey, OutpostRequestKindEnum kind, Guid agent, ReadOnlySpan<byte> body)
    {
        throw new NotSupportedException();
    }

    // Signs an answer with the server's key, over
    // `outpost/1 answer {kind} {agent} {status} {requestHash} {bodyHash}` (§3), so that the answer fits only the one
    // request it was made for.
    public static byte[] SignAnswer(
        ECDsa serverKey,
        OutpostRequestKindEnum kind,
        Guid agent,
        int status,
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> answerBody)
    {
        throw new NotSupportedException();
    }

    // Signs the exact bytes of a command with the server's key, over `outpost/1 command {bodyHash}` (§3).
    public static byte[] SignCommand(ECDsa serverKey, ReadOnlySpan<byte> commandBytes)
    {
        throw new NotSupportedException();
    }

    // Verifies the signature of a request with the agent's key, over the signing input of SignRequest.
    public static bool VerifyRequest(
        ECDsa agentKey,
        OutpostRequestKindEnum kind,
        Guid agent,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Verifies the signature of an answer with the server's key, over the signing input of SignAnswer.
    public static bool VerifyAnswer(
        ECDsa serverKey,
        OutpostRequestKindEnum kind,
        Guid agent,
        int status,
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> answerBody,
        ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }

    // Verifies the signature of the exact bytes of a command with the server's key, over the signing input of
    // SignCommand.
    public static bool VerifyCommand(ECDsa serverKey, ReadOnlySpan<byte> commandBytes, ReadOnlySpan<byte> signature)
    {
        throw new NotSupportedException();
    }
}
