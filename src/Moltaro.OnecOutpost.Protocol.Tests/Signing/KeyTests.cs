using System.Security.Cryptography;
using Moltaro.OnecOutpost.Protocol.Signing;

namespace Moltaro.OnecOutpost.Protocol.Tests.Signing;

// The public keys and the fingerprints of §2 of docs/protocol.md: a key is the 91-byte SubjectPublicKeyInfo of an ECDSA
// key on P-256 with an uncompressed point, and its fingerprint the 64 lowercase hexadecimal digits of SHA-256 over
// those bytes.
public sealed class KeyTests
{
    // The key of Sample with its point compressed: the same curve and the same x, in 59 bytes.
    private const string CompressedKeyHex =
        "3039301306072a8648ce3d020106082a8648ce3d03010703220003"
        + "14644602dad49abcdff37fff695f67b5f38eafa762d61419cccf0a4815b12ca5";

    [Fact]
    public void The_fingerprint_of_a_key_is_the_SHA256_of_its_91_bytes_in_lowercase_hexadecimal_digits()
    {
        Assert.Equal(Sample.PublicKeyFingerprint, OutpostKeys.Fingerprint(Sample.PublicKey));
    }

    [Fact]
    public void A_public_key_is_exported_as_the_91_bytes_of_its_SubjectPublicKeyInfo()
    {
        using ECDsa key = Sample.NewKey();

        byte[] exported = OutpostKeys.ExportPublicKey(key);

        Assert.Equal(91, exported.Length);
        Assert.Equal(key.ExportSubjectPublicKeyInfo(), exported);
    }

    [Fact]
    public void An_imported_key_verifies_what_its_private_key_signed()
    {
        using ECDsa key = Sample.NewKey();
        byte[] data = [1, 2, 3];
        byte[] signature = key.SignData(data, HashAlgorithmName.SHA256);

        using ECDsa imported = OutpostKeys.ImportPublicKey(key.ExportSubjectPublicKeyInfo());

        Assert.True(imported.VerifyData(data, signature, HashAlgorithmName.SHA256));
    }

    [Fact]
    public void The_key_of_the_sample_is_imported()
    {
        using ECDsa imported = OutpostKeys.ImportPublicKey(Sample.PublicKey);

        Assert.Equal(Sample.PublicKey, imported.ExportSubjectPublicKeyInfo());
    }

    [Fact]
    public void A_key_with_a_byte_beyond_its_structure_is_refused_with_form()
    {
        byte[] key = [.. Sample.PublicKey, 0];

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => OutpostKeys.ImportPublicKey(key));
    }

    [Fact]
    public void A_key_cut_short_is_refused_with_form()
    {
        byte[] key = Sample.PublicKey[..90];

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => OutpostKeys.ImportPublicKey(key));
    }

    [Fact]
    public void A_key_with_a_compressed_point_is_refused_with_form()
    {
        byte[] key = Convert.FromHexString(CompressedKeyHex);

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => OutpostKeys.ImportPublicKey(key));
    }

    [Fact]
    public void A_key_on_another_curve_is_refused_with_form()
    {
        using ECDsa other = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        byte[] key = other.ExportSubjectPublicKeyInfo();

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => OutpostKeys.ImportPublicKey(key));
    }

    [Fact]
    public void Bytes_of_the_right_length_that_are_no_key_are_refused_with_form()
    {
        byte[] key = new byte[91];

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => OutpostKeys.ImportPublicKey(key));
    }

    // Every byte of the structure is right, and only the point is not one of the curve: its y no longer fits its x.
    [Fact]
    public void A_key_whose_point_is_not_on_the_curve_is_refused_with_form()
    {
        byte[] key = Sample.PublicKey;
        key[^1] ^= 1;

        ProtocolAssert.Refused(OutpostProtocolCodes.Form, () => OutpostKeys.ImportPublicKey(key));
    }
}
