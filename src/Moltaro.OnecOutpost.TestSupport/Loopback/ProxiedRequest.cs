using Moltaro.OnecOutpost.Protocol;

namespace Moltaro.OnecOutpost.TestSupport.Loopback;

// A request of the agent as the TestProxy read it, whole: its kind, the path of its head line, its header lines but
// Host, and the exact bytes of its body. A rule of the proxy chooses a request by these, and a replay sends them again.
internal sealed record ProxiedRequest(
    OutpostRequestKindEnum Kind,
    string Path,
    IReadOnlyList<(string Name, string Value)> Headers,
    byte[] Body)
{
    // The bytes of its one Outpost-Signature in the form of §2 of docs/protocol.md, which an answer to it is signed
    // over; empty when it has none in that form.
    public byte[] Signature
    {
        get
        {
            string[] values = [.. Headers
                .Where(header => string.Equals(header.Name, OutpostProtocol.SignatureHeader, StringComparison.OrdinalIgnoreCase))
                .Select(header => header.Value)];

            return values.Length == 1 && OutpostHeaders.TryParseSignature(values[0], out byte[]? signature) ? signature : [];
        }
    }
}
