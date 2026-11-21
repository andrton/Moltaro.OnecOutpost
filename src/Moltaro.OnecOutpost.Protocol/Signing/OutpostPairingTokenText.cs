using System.Diagnostics.CodeAnalysis;

namespace Moltaro.OnecOutpost.Protocol.Signing;

// The parts of the pairing token of §5 of docs/protocol.md, `outpost1.{agent}.{secret}.{fingerprint}`: the identity
// of the pending agent, the 16 random bytes of the secret, and the fingerprint of the server key as 64 lowercase
// hexadecimal digits (§2).
internal sealed record OutpostPairingTokenText(Guid Agent, byte[] Secret, string Fingerprint)
{
    // Writes the token in the form of §5.
    public string Write()
    {
        throw new NotSupportedException();
    }

    // Reads a token of §5. False, without an exception, for a text with another prefix, another number of parts, or a
    // part of another form: such a text is not a token.
    public static bool TryRead(string text, [NotNullWhen(true)] out OutpostPairingTokenText? token)
    {
        throw new NotSupportedException();
    }

    // Leaves the secret out, so that a log line or a message that shows the record never holds it.
    public override string ToString()
    {
        throw new NotSupportedException();
    }
}
