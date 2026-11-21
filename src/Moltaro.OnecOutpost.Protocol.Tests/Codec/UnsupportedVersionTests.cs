using Moltaro.OnecOutpost.Protocol.Codec;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The one unsigned body of §9 of docs/protocol.md, `{"code":"protocol-version"}`, which a server sends for a path that
// names a version it does not speak. An agent recognizes exactly that body, for a reason of a lost answer and for the
// console, and acts on nothing because of it.
public sealed class UnsupportedVersionTests
{
    private const string Body = """{"code":"protocol-version"}""";

    [Fact]
    public void The_body_is_written_exactly_as_the_protocol_gives_it()
    {
        Assert.Equal(MessageText.Utf8(Body), OutpostCodec.WriteUnsupportedVersion());
    }

    [Fact]
    public void The_exact_body_is_recognized()
    {
        Assert.True(OutpostCodec.IsUnsupportedVersion(MessageText.Utf8(Body)));
        Assert.True(OutpostCodec.IsUnsupportedVersion(OutpostCodec.WriteUnsupportedVersion()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"code": "protocol-version"}""")]
    [InlineData("""{"code":"protocol-version"} """)]
    [InlineData(""" {"code":"protocol-version"}""")]
    [InlineData("""{"code":"protocol-version","time":"2026-11-20T10:15:00.123Z"}""")]
    [InlineData("""{"code":"protocol-versions"}""")]
    [InlineData("""{"code":"protocol-versio"}""")]
    [InlineData("""{"code":"form"}""")]
    [InlineData("""{"c\u006fde":"protocol-version"}""")]
    [InlineData("""{"code":"protocol-version"}{"code":"protocol-version"}""")]
    [InlineData("<html>400 Bad Request</html>")]
    public void Any_other_body_is_not_recognized_and_throws_nothing(string body)
    {
        Assert.False(OutpostCodec.IsUnsupportedVersion(MessageText.Utf8(body)));
    }

    [Fact]
    public void The_body_after_a_byte_order_mark_is_not_recognized()
    {
        byte[] body = [0xEF, 0xBB, 0xBF, .. MessageText.Utf8(Body)];

        Assert.False(OutpostCodec.IsUnsupportedVersion(body));
    }
}
