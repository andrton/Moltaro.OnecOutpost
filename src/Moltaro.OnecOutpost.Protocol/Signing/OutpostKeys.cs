using System.Security.Cryptography;

namespace Moltaro.OnecOutpost.Protocol.Signing;

// The public keys and the fingerprints of §2 of docs/protocol.md: a key is the 91-byte DER SubjectPublicKeyInfo of an
// ECDSA key on the named curve P-256 with an uncompressed point.
internal static class OutpostKeys
{
    private const int KeyLength = 91;

    // The 27 bytes that every key of §2 begins with, so that the 64 bytes of the point x ‖ y follow them: the DER
    // SEQUENCE of 89 bytes, the algorithm id-ecPublicKey (1.2.840.10045.2.1) on the named curve P-256
    // (1.2.840.10045.3.1.7), and the BIT STRING of 66 bytes with no unused bits, whose point is uncompressed (04).
    private static ReadOnlySpan<byte> KeyPrefix =>
    [
        0x30, 0x59,
        0x30, 0x13,
        0x06, 0x07, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x02, 0x01,
        0x06, 0x08, 0x2a, 0x86, 0x48, 0xce, 0x3d, 0x03, 0x01, 0x07,
        0x03, 0x42, 0x00,
        0x04,
    ];

    // Exports the public key of a key in the 91-byte form of §2, as ECDsa.ExportSubjectPublicKeyInfo writes it.
    public static byte[] ExportPublicKey(ECDsa key)
    {
        return key.ExportSubjectPublicKeyInfo();
    }

    // Imports a public key. It accepts exactly the 91-byte form of §2, and refuses anything else with
    // OutpostProtocolException and `form`: another key or curve, a compressed point, and bytes beyond the structure
    // included. The caller owns the key returned and disposes it.
    public static ECDsa ImportPublicKey(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        // The import of .NET reads other curves and compressed points too, and ignores bytes after the structure, so
        // the form is checked here. The prefix fixes every byte of the structure but the point.
        if (subjectPublicKeyInfo.Length != KeyLength || !subjectPublicKeyInfo.StartsWith(KeyPrefix))
        {
            throw new OutpostProtocolException(
                OutpostProtocolCodes.Form,
                "A key is not the 91-byte SubjectPublicKeyInfo of an ECDSA key on P-256 with an uncompressed point.");
        }

        ECDsa key = ECDsa.Create();

        try
        {
            key.ImportSubjectPublicKeyInfo(subjectPublicKeyInfo, out _);

            return key;
        }
        catch (Exception exception) when (exception is CryptographicException or PlatformNotSupportedException)
        {
            // What is left is the point, which the import checks lies on the curve. Windows reports a point that does
            // not as PlatformNotSupportedException.
            key.Dispose();

            throw new OutpostProtocolException(OutpostProtocolCodes.Form, "A key is no point on the curve P-256.");
        }
    }

    // The fingerprint of a key (§2): the 64 lowercase hexadecimal digits of SHA-256 over its 91 bytes. It does not
    // check that the bytes are a key.
    public static string Fingerprint(ReadOnlySpan<byte> subjectPublicKeyInfo)
    {
        return Convert.ToHexStringLower(SHA256.HashData(subjectPublicKeyInfo));
    }
}
