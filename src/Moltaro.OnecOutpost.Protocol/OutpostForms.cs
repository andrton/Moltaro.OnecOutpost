using System.Diagnostics.CodeAnalysis;

namespace Moltaro.OnecOutpost.Protocol;

// The forms of §2 of docs/protocol.md that the headers, the pairing token and the codec all need: canonical base64url
// and the 36-character lowercase identity. They live here once, in the root, so that neither the signing types nor
// the codec needs the other for them.
internal static class OutpostForms
{
    // Writes a binary value as §2 says: base64url without padding (RFC 4648 §5).
    public static string WriteBase64Url(ReadOnlySpan<byte> bytes)
    {
        throw new NotSupportedException();
    }

    // Reads a binary value of §2. False, without an exception, for any text but canonical base64url without padding:
    // a character outside the alphabet, padding, a length that no bytes give and non-zero padding bits included.
    public static bool TryReadBase64Url(ReadOnlySpan<char> text, [NotNullWhen(true)] out byte[]? bytes)
    {
        throw new NotSupportedException();
    }

    // Writes an identity as §2 says: 36 lowercase characters in the form xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx.
    public static string WriteId(Guid id)
    {
        throw new NotSupportedException();
    }

    // Reads an identity of §2. False, without an exception, for any text but exactly the form that WriteId writes:
    // uppercase digits, braces and every other form of a GUID are refused.
    public static bool TryReadId(ReadOnlySpan<char> text, out Guid id)
    {
        throw new NotSupportedException();
    }
}
