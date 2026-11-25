using Moltaro.OnecOutpost.Protocol;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

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
    // The answer that the response gives to the request of the kind, with its body read whole. A response with two
    // lines of Outpost-Signature throws, which no node sends.
    public static async Task<AgentAnswer> ReadAsync(
        HttpResponseMessage response,
        OutpostRequestKindEnum kind,
        byte[] requestBody,
        byte[] requestSignature,
        CancellationToken cancellationToken)
    {
        string? signature = null;

        if (response.Headers.TryGetValues(OutpostProtocol.SignatureHeader, out IEnumerable<string>? values))
        {
            signature = values.Single();
        }

        byte[] body = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        return new AgentAnswer(
            kind,
            (int)response.StatusCode,
            response.Content.Headers.ContentType?.ToString(),
            signature,
            body,
            requestBody,
            requestSignature);
    }

    // A bare answer carries only the HTTP status (§9 of docs/protocol.md).
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
