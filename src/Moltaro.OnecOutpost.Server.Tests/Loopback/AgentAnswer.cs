using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.Server.Tests.Loopback;

// What came back for a request of a TestAgent: the status, the Content-Type and the Outpost-Signature of the answer,
// when it has them, and its body; with the kind, the exact bytes and the signature of the request, which a signed
// answer is verified against and a replay sends again.
internal sealed record AgentAnswer(
    OutpostRequestKindEnum Kind,
    int Status,
    string? ContentType,
    string? Signature,
    byte[] Body,
    byte[] RequestBody,
    byte[] RequestSignature)
{
    // A status alone, without a body or an Outpost-Signature, as the brief of S4 defines a bare answer.
    public bool IsBare => Signature is null && Body.Length == 0;

    // The bytes of the Outpost-Signature. An answer without one, or with one that is not in the form of §2 of
    // docs/protocol.md, fails the test.
    public byte[] SignatureBytes
    {
        get
        {
            Assert.True(
                OutpostHeaders.TryParseSignature(Signature, out byte[]? signature),
                "The answer is not signed.");

            return signature;
        }
    }
}
