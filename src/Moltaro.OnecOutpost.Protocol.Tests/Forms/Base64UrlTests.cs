using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Forms;

// The binary values of §2 of docs/protocol.md: base64url without padding (RFC 4648 §5). A reader refuses any other
// form, non-zero padding bits included, so that one value has one text.
public sealed class Base64UrlTests
{
    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("00", "AA")]
    [InlineData("0001", "AAE")]
    [InlineData("000102", "AAEC")]

    // The two characters that differ from base64: - for + and _ for /.
    [InlineData("fbff", "-_8")]
    [InlineData("000102030405060708090a0b0c0d0e0f", "AAECAwQFBgcICQoLDA0ODw")]
    public void Bytes_are_written_in_the_alphabet_of_base64url_without_padding(string bytesInHex, string text)
    {
        Assert.Equal(text, OutpostForms.WriteBase64Url(Convert.FromHexString(bytesInHex)));
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("AA", "00")]
    [InlineData("AAE", "0001")]
    [InlineData("AAEC", "000102")]
    [InlineData("-_8", "fbff")]
    [InlineData("AAECAwQFBgcICQoLDA0ODw", "000102030405060708090a0b0c0d0e0f")]
    public void A_canonical_text_is_read_into_its_bytes(string text, string bytesInHex)
    {
        Assert.True(OutpostForms.TryReadBase64Url(text, out byte[]? bytes));

        Assert.Equal(Convert.FromHexString(bytesInHex), bytes);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Bytes_of_every_length_round_trip()
    {
        for (int length = 1; length <= 100; length++)
        {
            byte[] written = System.Security.Cryptography.RandomNumberGenerator.GetBytes(length);

            Assert.True(OutpostForms.TryReadBase64Url(OutpostForms.WriteBase64Url(written), out byte[]? read));

            Assert.Equal(written, read);
        }
    }

    [Theory(Skip = SkipReason.NotImplemented)]

    // Padding.
    [InlineData("AA==")]
    [InlineData("AAE=")]

    // The alphabet of base64, not of base64url.
    [InlineData("+/8")]

    // A length that no bytes give.
    [InlineData("A")]
    [InlineData("AAECA")]

    // Padding bits that are not zero: the last character of one byte, and of two.
    [InlineData("AB")]
    [InlineData("AAF")]

    // A character outside the alphabet.
    [InlineData("AA EC")]
    [InlineData("AAEC\n")]
    [InlineData("AAE.")]
    public void A_text_of_any_other_form_is_not_read_and_throws_nothing(string text)
    {
        Assert.False(OutpostForms.TryReadBase64Url(text, out byte[]? bytes));
        Assert.Null(bytes);
    }
}
