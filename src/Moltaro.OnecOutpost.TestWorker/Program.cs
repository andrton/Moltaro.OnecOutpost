using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Moltaro.OnecOutpost.TestWorker;

// The test program that stands in for the COM worker of S9 in the agent's tests with Requires=Windows (the S9b brief).
// It speaks the worker's lines, command line and exit codes over its raw standard streams, one JSON object per line in
// UTF-8 ended with LF, and runs no 1C:
// - the command line is <base key> <read|write>, and anything else ends it with exit code 1;
// - the end of its input ends it with exit code 0, before the first line and after an outcome;
// - a line it cannot read ends it with exit code 1;
// - the location of the connection line decides the ready line: no-session gets {"ready":false} and exit code 2,
//   line <text> gets <text> as its ready line, and any other {"ready":true};
// - each command line runs its script, and each outcome is the executor's line, {"size":<number>,"outcome":<object>},
//   with the true size of the object.
//
// The scripts: wait writes nothing until the input ends or a kill ends the program; exit <n> ends it with n and no
// outcome; line <text> writes <text> as the line of its outcome; pid, path, arguments and connection succeed with the
// identity of the process, its Environment.ProcessPath, its command line as an array and the members of its connection
// line as an object; any other script succeeds with null.
internal static class Program
{
    private const string LinePrefix = "line ";

    private const string ExitPrefix = "exit ";

    private static int Main(string[] arguments)
    {
        if (arguments is not [{ Length: > 0 }, "read" or "write"])
        {
            return 1;
        }

        // The streams live as long as the process, so nothing disposes of them.
        Stream input = new BufferedStream(Console.OpenStandardInput());
        Stream output = Console.OpenStandardOutput();

        byte[]? connection = ReadLine(input);

        if (connection is null)
        {
            return 0;
        }

        string? location = StringMember(connection, "location");

        if (location is null)
        {
            return 1;
        }

        if (location == "no-session")
        {
            WriteLine(output, """{"ready":false}""");

            return 2;
        }

        bool ownReady = location.StartsWith(LinePrefix, StringComparison.Ordinal);

        WriteLine(output, ownReady ? location[LinePrefix.Length..] : """{"ready":true}""");

        while (true)
        {
            byte[]? line = ReadLine(input);

            if (line is null)
            {
                return 0;
            }

            string? script = StringMember(line, "script");

            if (script is null)
            {
                return 1;
            }

            if (script == "wait")
            {
                // Nothing is written; what the input still brings is read and dropped until it ends.
                while (ReadLine(input) is not null)
                {
                }

                return 0;
            }

            if (script.StartsWith(ExitPrefix, StringComparison.Ordinal))
            {
                return int.Parse(script[ExitPrefix.Length..], CultureInfo.InvariantCulture);
            }

            if (script.StartsWith(LinePrefix, StringComparison.Ordinal))
            {
                WriteLine(output, script[LinePrefix.Length..]);

                continue;
            }

            WriteLine(output, OutcomeLine(script, arguments, connection));
        }
    }

    // The next line of the input without its LF, or null when the input ended before a byte of it. A last line
    // without its LF is a line.
    private static byte[]? ReadLine(Stream input)
    {
        ArrayBufferWriter<byte> line = new();

        while (true)
        {
            int next = input.ReadByte();

            if (next < 0)
            {
                return line.WrittenCount == 0 ? null : line.WrittenSpan.ToArray();
            }

            if (next == '\n')
            {
                return line.WrittenSpan.ToArray();
            }

            line.Write([(byte)next]);
        }
    }

    // The string member of a line that is a JSON object, or null when the line is not one or has no such string.
    private static string? StringMember(byte[] line, string name)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);

            return document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty(name, out JsonElement value)
                && value.ValueKind == JsonValueKind.String
                ? value.GetString()
                : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // The executor's line of a script that succeeds with what it gives: {"size":<number>,"outcome":<object>}.
    private static string OutcomeLine(string script, string[] arguments, byte[] connection)
    {
        ArrayBufferWriter<byte> outcome = new();

        using (Utf8JsonWriter writer = new(outcome))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", "succeeded");
            writer.WriteNull("code");
            writer.WritePropertyName("result");
            WriteResult(writer, script, arguments, connection);
            writer.WriteNull("errorText");
            writer.WriteStartArray("messages");
            writer.WriteEndArray();
            writer.WriteBoolean("messagesTrimmed", false);
            writer.WriteEndObject();
        }

        ArrayBufferWriter<byte> line = new();

        using (Utf8JsonWriter writer = new(line))
        {
            writer.WriteStartObject();
            writer.WriteNumber("size", outcome.WrittenCount);
            writer.WritePropertyName("outcome");
            writer.WriteRawValue(outcome.WrittenSpan, skipInputValidation: true);
            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(line.WrittenSpan);
    }

    private static void WriteResult(Utf8JsonWriter writer, string script, string[] arguments, byte[] connection)
    {
        switch (script)
        {
            case "pid":
                writer.WriteNumberValue(Environment.ProcessId);
                break;

            case "path":
                writer.WriteStringValue(Environment.ProcessPath);
                break;

            case "arguments":
                writer.WriteStartArray();

                foreach (string argument in arguments)
                {
                    writer.WriteStringValue(argument);
                }

                writer.WriteEndArray();
                break;

            case "connection":
                writer.WriteRawValue(connection);
                break;

            default:
                writer.WriteNullValue();
                break;
        }
    }

    // The line with its LF in one write, as the worker writes one.
    private static void WriteLine(Stream output, string line)
    {
        output.Write(Encoding.UTF8.GetBytes(line + "\n"));
        output.Flush();
    }
}
