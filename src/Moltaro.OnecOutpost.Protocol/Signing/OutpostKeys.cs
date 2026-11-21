using System.Security.Cryptography;

namespace Moltaro.OnecOutpost.Protocol.Signing;

// The public keys and the fingerprints of §2 of docs/protocol.md: a key is the 91-byte DER SubjectPublicKeyInfo of an
// ECDSA key on the named curve P-256 with an uncompressed point.
internal static class OutpostKeys
{
    // Exports the public key of a key in the 91-byte form of §2, as ECDsa.ExportSubjectPublicKeyInfo writes it.
    public static byte[] ExportPublicKey(ECDsa key)
    {
        throw new NotSupportedException();
    }

    // Imports a public key. It accepts exactly the 91-byte form of §2, and refuses anything else with
    // OutpostProtocolException and `form`: another key or curve, a compressed point, and bytes beyond the structure
    // included. The caller owns the key returned and disposes it.
    public static ECDsa ImportPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        throw new NotSupportedException();
    }

    // The fingerprint of a key (§2): the 64 lowercase hexadecimal digits of SHA-256 over its 91 bytes. It does not
    // check that the bytes are a key.
    public static string Fingerprint(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        throw new NotSupportedException();
    }
}
