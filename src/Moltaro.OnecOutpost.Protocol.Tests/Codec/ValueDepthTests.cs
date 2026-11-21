using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Codec;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The levels of §2 of docs/protocol.md that parameters and a result nest, counted from their own outer value: a scalar
// is level 0, and an object or an array at the top is level 1.
public sealed class ValueDepthTests
{
    [Theory]
    [InlineData("null")]
    [InlineData("true")]
    [InlineData("12")]
    [InlineData("\"text\"")]
    public void A_scalar_is_level_0(string value)
    {
        Assert.Equal(0, Depth(value));
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("[]")]
    [InlineData("{\"a\":1,\"b\":\"text\"}")]
    [InlineData("[1,2,3]")]
    public void An_object_or_an_array_of_scalars_is_level_1(string value)
    {
        Assert.Equal(1, Depth(value));
    }

    [Fact]
    public void Objects_and_arrays_count_alike_and_the_deepest_member_decides()
    {
        Assert.Equal(4, Depth("{\"a\":1,\"b\":[[{}],2],\"c\":{\"d\":[]}}"));
    }

    [Theory]
    [InlineData(64)]
    [InlineData(65)]
    public void A_value_at_the_limit_and_one_beyond_it_are_counted_exactly(int levels)
    {
        Assert.Equal(levels, Depth(MessageText.Nested(levels)));
    }

    private static int Depth(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value, new JsonDocumentOptions { MaxDepth = 128 });

        return OutpostCodec.ValueDepth(document.RootElement);
    }
}
