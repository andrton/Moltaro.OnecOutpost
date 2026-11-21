using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Signing;

// The pairing token of §5 of docs/protocol.md, `outpost1.{agent}.{secret}.{fingerprint}`: the identity of the pending
// agent, 16 bytes of secret as 22 characters of base64url, and the fingerprint of the server key. A text of any other
// form is not a token.
public sealed class PairingTokenTextTests
{
    // The bytes 00 01 … 0f are AAECAwQFBgcICQoLDA0ODw in base64url.
    private const string SecretText = "AAECAwQFBgcICQoLDA0ODw";

    private const string TokenText =
        "outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91";

    private static readonly byte[] Secret = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15];

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_token_is_written_as_its_prefix_its_identity_its_secret_and_its_fingerprint_separated_by_dots()
    {
        OutpostPairingTokenText token = new(Sample.Agent, Secret, Sample.PublicKeyFingerprint);

        Assert.Equal(TokenText, token.Write());
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_token_is_read_into_its_parts()
    {
        Assert.True(OutpostPairingTokenText.TryRead(TokenText, out OutpostPairingTokenText? token));

        Assert.Equal(Sample.Agent, token.Agent);
        Assert.Equal(Secret, token.Secret);
        Assert.Equal(Sample.PublicKeyFingerprint, token.Fingerprint);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_token_round_trips()
    {
        byte[] secret = System.Security.Cryptography.RandomNumberGenerator.GetBytes(16);
        OutpostPairingTokenText written = new(Guid.NewGuid(), secret, Sample.PublicKeyFingerprint);

        Assert.True(OutpostPairingTokenText.TryRead(written.Write(), out OutpostPairingTokenText? read));

        Assert.Equal(written.Agent, read.Agent);
        Assert.Equal(secret, read.Secret);
        Assert.Equal(written.Fingerprint, read.Fingerprint);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("")]

    // Another prefix.
    [InlineData("outpost2.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]
    [InlineData("Outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]
    [InlineData("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]

    // Another number of parts.
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91.")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91.x")]

    // An identity in another form: uppercase, and without its hyphens.
    [InlineData("outpost1.3F2B6C1E-8A4D-4C2F-9B7E-1D0A5E6F7C8B.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]
    [InlineData("outpost1.3f2b6c1e8a4d4c2f9b7e1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]

    // A secret that is not 16 bytes: 15 and 18; one with padding; one whose last character has padding bits set.
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0O.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODxAR.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw==.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODx.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]

    // A fingerprint in another form: uppercase, 63 digits, 65 digits, and a character that is no hexadecimal digit.
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.F7EDD86F02E6FA4BCCF37F636314D4A011B3BD5E7863DF2AC582063062250A91")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a9")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a910")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.g7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]

    // White space around a token.
    [InlineData(" outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91")]
    [InlineData("outpost1.3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b.AAECAwQFBgcICQoLDA0ODw.f7edd86f02e6fa4bccf37f636314d4a011b3bd5e7863df2ac582063062250a91\n")]
    public void A_text_of_another_form_is_not_read_and_throws_nothing(string text)
    {
        Assert.False(OutpostPairingTokenText.TryRead(text, out OutpostPairingTokenText? token));
        Assert.Null(token);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_text_that_shows_a_token_leaves_the_secret_out()
    {
        OutpostPairingTokenText token = new(Sample.Agent, Secret, Sample.PublicKeyFingerprint);

        string shown = token.ToString();

        Assert.DoesNotContain(SecretText, shown);
        Assert.NotEqual(TokenText, shown);
    }
}
