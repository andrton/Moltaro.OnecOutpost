using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Forms;

// The identities of §2 of docs/protocol.md: a GUID as 36 lowercase characters in the form
// xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx, in JSON and in headers alike. No other form of a GUID is an identity.
public sealed class IdentityFormTests
{
    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_identity_is_written_as_36_lowercase_characters()
    {
        Guid id = Guid.Parse("3F2B6C1E-8A4D-4C2F-9B7E-1D0A5E6F7C8B");

        Assert.Equal("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b", OutpostForms.WriteId(id));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public void An_identity_in_its_form_is_read()
    {
        Assert.True(OutpostForms.TryReadId("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b", out Guid id));

        Assert.Equal(Guid.Parse("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b"), id);
    }

    [Theory(Skip = SkipReason.NotImplemented)]
    [InlineData("")]

    // Uppercase digits, all of them and one of them.
    [InlineData("3F2B6C1E-8A4D-4C2F-9B7E-1D0A5E6F7C8B")]
    [InlineData("3F2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b")]

    // The other forms that .NET reads as a GUID.
    [InlineData("{3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b}")]
    [InlineData("(3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b)")]
    [InlineData("3f2b6c1e8a4d4c2f9b7e1d0a5e6f7c8b")]
    [InlineData(" 3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b")]
    [InlineData("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b ")]

    // A digit less, a digit more, a hyphen out of its place and a character that is no hexadecimal digit.
    [InlineData("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8")]
    [InlineData("3f2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b0")]
    [InlineData("3f2b6c1e8-a4d-4c2f-9b7e-1d0a5e6f7c8b")]
    [InlineData("3g2b6c1e-8a4d-4c2f-9b7e-1d0a5e6f7c8b")]
    public void A_text_of_any_other_form_is_not_read_and_throws_nothing(string text)
    {
        Assert.False(OutpostForms.TryReadId(text, out _));
    }
}
