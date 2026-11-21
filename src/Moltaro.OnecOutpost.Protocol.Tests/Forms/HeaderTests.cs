using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Forms;

// The values of the headers of §4 of docs/protocol.md: `Outpost-Agent` is an identity in the form of §2, and
// `Outpost-Signature` the 64 bytes of a signature as 86 characters of base64url.
public sealed class HeaderTests
{
    // 64 bytes of zero are 86 times the character A, and its last character carries four padding bits.
    private static readonly string ZeroSignature = new('A', 86);

    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_identity_of_an_agent_is_written_in_the_form_of_identities()
    {
        Assert.Equal(Sample.AgentText, OutpostHeaders.FormatAgent(Sample.Agent));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_identity_in_its_form_is_read()
    {
        Assert.True(OutpostHeaders.TryParseAgent(Sample.AgentText, out Guid agent));

        Assert.Equal(Sample.Agent, agent);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("3F2B6C1E-8A4D-4C2F-9B7E-1D0A5E6F7C8B")]
    [InlineData("{3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b}")]
    [InlineData("3f2b6c1e8a4d4c2f9b7e1d0a5e6f7c8b")]
    public void An_identity_that_is_missing_or_in_another_form_is_not_read(string? value)
    {
        Assert.False(OutpostHeaders.TryParseAgent(value, out _));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_signature_is_written_as_86_characters_and_read_back()
    {
        byte[] signature = System.Security.Cryptography.RandomNumberGenerator.GetBytes(64);

        string value = OutpostHeaders.FormatSignature(signature);

        Assert.Equal(MessageText.Base64Url(signature), value);
        Assert.Equal(86, value.Length);
        Assert.True(OutpostHeaders.TryParseSignature(value, out byte[]? read));
        Assert.Equal(signature, read);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_signature_of_exactly_64_bytes_is_read()
    {
        Assert.True(OutpostHeaders.TryParseSignature(ZeroSignature, out byte[]? signature));

        Assert.Equal(new byte[64], signature);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_missing_signature_is_not_read()
    {
        Assert.False(OutpostHeaders.TryParseSignature(null, out byte[]? signature));
        Assert.Null(signature);
    }

    [Theory(Skip = SkipReason.NotImplemented)]

    // 0, 63 and 65 bytes.
    [InlineData(0)]
    [InlineData(84)]
    [InlineData(87)]
    public void A_signature_of_another_length_is_not_read(int characters)
    {
        Assert.False(OutpostHeaders.TryParseSignature(new string('A', characters), out _));
    }

    [Theory(Skip = SkipReason.NotImplemented)]

    // Padding bits that are not zero; the alphabet of base64; padding, as base64 writes 64 bytes; white space.
    [InlineData("B", "")]
    [InlineData("A", "==")]
    [InlineData("+", "")]
    [InlineData("A", " ")]
    public void A_signature_of_64_bytes_in_another_form_is_not_read(string lastCharacter, string after)
    {
        string value = new string('A', 85) + lastCharacter + after;

        Assert.False(OutpostHeaders.TryParseSignature(value, out _));
    }
}
