using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Protocol.Codec;

namespace Moltaro.OnecOutpost.Agent.Tests.Transport;

// The one judgment of the unsupported version's body (the S10 brief, "Internal of S10c", review 3's entry 3), which the
// poll, the outcome and pairing read: an answer is the server's refusal of the protocol version only as a 400 whose
// body came whole and is exactly the unsigned body of §9, whatever its other headers; the same body with another
// status, another body, or a body that did not come whole is not.
public sealed class ServerAnswerTests
{
    [Fact]
    public void A_400_whose_body_is_the_unsigned_body_of_the_protocol_version_is_the_refusal_of_the_version()
    {
        Assert.True(new ServerAnswer(400, OutpostCodec.WriteUnsupportedVersion(), IsJson: true, []).IsUnsupportedVersion);
        Assert.True(new ServerAnswer(400, OutpostCodec.WriteUnsupportedVersion(), IsJson: false, []).IsUnsupportedVersion);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(200)]
    public void The_same_body_with_another_status_is_not_the_refusal_of_the_version(int status)
    {
        Assert.False(new ServerAnswer(status, OutpostCodec.WriteUnsupportedVersion(), IsJson: true, []).IsUnsupportedVersion);
    }

    [Fact]
    public void A_400_with_another_body_or_without_a_whole_body_is_not_the_refusal_of_the_version()
    {
        byte[] other = [.. OutpostCodec.WriteUnsupportedVersion(), (byte)' '];

        Assert.False(new ServerAnswer(400, other, IsJson: true, []).IsUnsupportedVersion);
        Assert.False(new ServerAnswer(400, null, IsJson: true, []).IsUnsupportedVersion);
        Assert.False(new ServerAnswer(400, [], IsJson: true, []).IsUnsupportedVersion);
    }
}
