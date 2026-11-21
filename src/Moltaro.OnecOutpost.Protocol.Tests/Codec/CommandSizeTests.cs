using System.Text.Json;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Messages;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Protocol.Tests.Codec;

// The size of a command (§7 of docs/protocol.md): the text of its script between its quotes plus the text of its
// parameters, both in UTF-8 as the writer of §2 writes them into the command, every escape counted as it is written.
// Parameters without members are the 2 bytes of {}.
public sealed class CommandSizeTests
{
    // Scripts and parameters with what the writer of §2 escapes and with what it writes as it is.
    public static TheoryData<string, string> ScriptsAndParameters => new()
    {
        { "a", "{}" },
        { "Result = Parameters[\"date\"];", "{\"date\":\"2026-11-20\"}" },
        { "A\\B \"quoted\"\tand\r\na form feed \f, a backspace \b", "{\"text\":\"\\\"\\\\\\b\\t\\n\\f\\r\"}" },
        { "Controls \u0001 and \u001f", "{\"\\u0001\":\"\\u001f\"}" },
        { "Result = \"\u00e9\U0001F600\u2028/<>&'+\u007f\";", "{\"\u00e9\":[\"\U0001F600\",1,2.5,true,null,{\"deep\":[]}]}" },
        { "a", "{ \"escaped\" : \"\\u00e9\\ud83d\\ude00\\/\" , \"number\" : 12 }" },
    };

    [Fact]
    public void A_command_without_parameters_counts_its_script_and_the_2_bytes_of_the_empty_object()
    {
        Assert.Equal(3, OutpostCodec.CommandSize("a", Parse("{}")));
    }

    [Theory]

    // As they are: ASCII is 1 byte, U+00E9 is 2, U+2028 is 3, U+1F600 is 4.
    [InlineData("a/<>&'+\u007f", 8)]
    [InlineData("\u00e9", 2)]
    [InlineData("\u2028", 3)]
    [InlineData("\U0001F600", 4)]

    // The escapes of two characters.
    [InlineData("\"", 2)]
    [InlineData("\\", 2)]
    [InlineData("\b\t\n\f\r", 10)]

    // Every other control character is the 6 characters of \u00XX.
    [InlineData("\u0001", 6)]
    [InlineData("\u001f", 6)]
    public void The_text_of_a_script_counts_as_the_writer_writes_it(string script, int size)
    {
        Assert.Equal(size + 2, OutpostCodec.CommandSize(script, Parse("{}")));
    }

    [Theory]

    // {"a":[1,"é"]} is 14 bytes: the white space of the text it was read from does not travel.
    [InlineData("{ \"a\" : [ 1 , \"\u00e9\" ] }", 14)]

    // {"a":"é\n"} is 12 bytes: an escape that the writer does not write counts as the character, and one that it writes
    // as the escape.
    [InlineData("{\"\\u0061\":\"\\u00e9\\n\"}", 12)]

    // {"\u0001":"\"\\"} is 17 bytes: the name of a member counts like any other string.
    [InlineData("{\"\\u0001\":\"\\\"\\\\\"}", 17)]
    public void The_text_of_the_parameters_counts_as_the_writer_writes_it(string parameters, int size)
    {
        Assert.Equal(1 + size, OutpostCodec.CommandSize("a", Parse(parameters)));
    }

    // The size is the one number of both the server's check and the writer (§7): what a script and parameters add to
    // the bytes of a command is exactly what they add to its size.
    [Theory(Skip = SkipReason.NotImplemented)]
    [MemberData(nameof(ScriptsAndParameters))]
    public void The_size_counts_exactly_the_bytes_that_the_writer_writes_for_the_script_and_the_parameters(
        string script,
        string parameters)
    {
        int smallest = OutpostCodec.Write(Command("a", "{}")).Length;
        int written = OutpostCodec.Write(Command(script, parameters)).Length;

        Assert.Equal(written - smallest, OutpostCodec.CommandSize(script, Parse(parameters)) - 3);
    }

    private static OutpostCommandMessage Command(string script, string parameters)
    {
        return new OutpostCommandMessage(
            OutpostProtocol.Version,
            Sample.Command,
            Sample.Agent,
            "accounting",
            OutpostModeEnum.Read,
            null,
            script,
            Parse(parameters),
            new DateTimeOffset(2026, 11, 20, 10, 15, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 11, 20, 10, 15, 30, TimeSpan.Zero),
            TimeSpan.FromMinutes(2),
            16 * 1024 * 1024);
    }

    private static JsonElement Parse(string value)
    {
        using JsonDocument document = JsonDocument.Parse(value);

        return document.RootElement.Clone();
    }
}
