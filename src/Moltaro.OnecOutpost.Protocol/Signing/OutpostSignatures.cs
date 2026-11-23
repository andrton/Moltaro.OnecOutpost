using System.Globalization;
using System.Security.Cryptography;
using System.Text;

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
        return Sign(agentKey, RequestInput(kind, agent, body));
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
        return Sign(serverKey, AnswerInput(kind, agent, status, requestBody, answerBody));
    }

    // Signs the exact bytes of a command with the server's key, over `outpost/1 command {bodyHash}` (§3).
    public static byte[] SignCommand(ECDsa serverKey, ReadOnlySpan<byte> commandBytes)
    {
        return Sign(serverKey, CommandInput(commandBytes));
    }

    // Verifies the signature of a request with the agent's key, over the signing input of SignRequest.
    public static bool VerifyRequest(
        ECDsa agentKey,
        OutpostRequestKindEnum kind,
        Guid agent,
        ReadOnlySpan<byte> body,
        ReadOnlySpan<byte> signature)
    {
        return Verify(agentKey, RequestInput(kind, agent, body), signature);
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
        return Verify(serverKey, AnswerInput(kind, agent, status, requestBody, answerBody), signature);
    }

    // Verifies the signature of the exact bytes of a command with the server's key, over the signing input of
    // SignCommand.
    public static bool VerifyCommand(ECDsa serverKey, ReadOnlySpan<byte> commandBytes, ReadOnlySpan<byte> signature)
    {
        return Verify(serverKey, CommandInput(commandBytes), signature);
    }

    // The signing inputs of §3: ASCII fields separated by single spaces, each of a fixed form, so that no input can be
    // read as another. The identity has 36 characters, a hash 64 lowercase hexadecimal digits, and an HTTP status
    // three digits.
    private static byte[] RequestInput(OutpostRequestKindEnum kind, Guid agent, ReadOnlySpan<byte> body)
    {
        return Encoding.ASCII.GetBytes($"outpost/1 request {OutpostForms.WriteRequestKind(kind)} {OutpostForms.WriteId(agent)} {Hash(body)}");
    }

    private static byte[] AnswerInput(
        OutpostRequestKindEnum kind,
        Guid agent,
        int status,
        ReadOnlySpan<byte> requestBody,
        ReadOnlySpan<byte> answerBody)
    {
        string input = string.Create(
            CultureInfo.InvariantCulture,
            $"outpost/1 answer {OutpostForms.WriteRequestKind(kind)} {OutpostForms.WriteId(agent)} {status} {Hash(requestBody)} {Hash(answerBody)}");

        return Encoding.ASCII.GetBytes(input);
    }

    private static byte[] CommandInput(ReadOnlySpan<byte> commandBytes)
    {
        return Encoding.ASCII.GetBytes($"outpost/1 command {Hash(commandBytes)}");
    }

    // SHA-256 over the exact bytes, as 64 lowercase hexadecimal digits.
    private static string Hash(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexStringLower(SHA256.HashData(bytes));
    }

    private static byte[] Sign(ECDsa key, byte[] input)
    {
        return key.SignData(input, HashAlgorithmName.SHA256, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }

    // A signature in another form, such as DER, or of another length does not verify.
    private static bool Verify(ECDsa key, byte[] input, ReadOnlySpan<byte> signature)
    {
        return key.VerifyData(
            input,
            signature,
            HashAlgorithmName.SHA256,
            DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}
