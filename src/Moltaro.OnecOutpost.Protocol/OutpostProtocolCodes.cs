using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Protocol;

// The codes of OutpostProtocolException: why the codec refused what it read. All but ServerKey are also the codes of
// the refusals of §9 of docs/protocol.md.
internal static class OutpostProtocolCodes
{
    // A verified body breaks §2 or the rules of its message (§9).
    public const string Form = "form";

    // A signature of §3 does not verify (§9). Nothing of such bytes was parsed.
    public const string Signature = "signature";

    // An outcome beyond its limit (§6.3, §9).
    public const string ResultTooLarge = OutpostOutcomeCodes.ResultTooLarge;

    // A version of the protocol that this build does not speak (§7, §9).
    public const string ProtocolVersion = OutpostOutcomeCodes.ProtocolVersion;

    // `ServerKey` alone is a code of the codec only, never sent (§6.1): the server key of a pairing answer does not
    // match the fingerprint of the token.
    public const string ServerKey = "server-key";
}
