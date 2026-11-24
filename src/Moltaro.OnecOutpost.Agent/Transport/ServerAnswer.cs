using System.Net;
using Moltaro.OnecOutpost.Protocol.Codec;

namespace Moltaro.OnecOutpost.Agent.Transport;

// An answer of the server, or of anything on the path, as the agent read it: its status; its body when it came whole
// within its read limit and its wait (§10 of docs/protocol.md), and null otherwise; whether its Content-Type is exactly
// application/json; and the bytes of its Outpost-Signature, empty when it has none in the form of §2, so that they
// verify nothing. Nothing in it is trusted before a reader of the codec verified it.
internal sealed record ServerAnswer(int Status, byte[]? Body, bool IsJson, byte[] Signature)
{
    // A 400 whose unsigned body is the one of §9 for a protocol version the server does not support.
    public bool IsUnsupportedVersion => Status == (int)HttpStatusCode.BadRequest
        && Body is byte[] body
        && OutpostCodec.IsUnsupportedVersion(body);
}
