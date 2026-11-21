using System.Buffers;
using System.Text;
using System.Text.Json;

namespace Moltaro.OnecOutpost.ComWorker;

// The whole behavior of the worker program over its standard streams. It reads the connection line, opens the session
// and answers whether it is ready; then, for each command line, it opens a fresh executor, calls Run, writes the line
// that Run returned and disposes of the executor. The worker depends on nothing outside .NET (principle 12), so it
// reads and writes its lines with System.Text.Json and shares no type with the protocol assembly. The loop is
// synchronous: the worker is one thread that owns one COM session and has nothing else to do while a script runs.
internal sealed class Worker
{
    // The kinds and codes of the outcomes the worker writes itself (§8 of docs/protocol.md).
    private const string Failed = "failed";
    private const string Unknown = "unknown";
    private const string BaseUnreachable = "base-unreachable";
    private const string ConnectionLost = "connection-lost";
    private const string ResultType = "result-type";

    private const string PasswordPlaceholder = "<password>";

    // Opens the one session of the worker; ComConnector in the program, a fake in the tests.
    private readonly IOneCConnector _connector;

    // The mode of the command line. A Read worker refuses to run without safe mode, and the outcomes the worker writes
    // itself are Failed for a Read and Unknown for a Write where the two differ.
    private readonly WorkerModeEnum _mode;

    public Worker(IOneCConnector connector, WorkerModeEnum mode)
    {
        _connector = connector;
        _mode = mode;
    }

    // The mode as the executor's Run takes it.
    private string ModeText => _mode == WorkerModeEnum.Read ? "read" : "write";

    // A Read whose outcome the worker cannot give fails; a Write may have committed, so it is unknown.
    private string FailedOrUnknown => _mode == WorkerModeEnum.Read ? Failed : Unknown;

    // Runs the worker until its input ends or a fault ends it, and returns one of WorkerExitCodes. The input and the
    // output carry one JSON object per line in UTF-8, ended with LF, without a BOM; the output carries these lines and
    // nothing else, and an outcome line goes out as the executor returned it, with its LF, in one write. The worker
    // reads a line to a depth of 72 levels, the protocol's limit of a message, so that parameters of 64 levels pass.
    // The diagnostics are for a person: the first input line is never written there, and the password is replaced
    // with <password> in every text written there.
    public int Run(Stream input, Stream output, TextWriter diagnostics)
    {
        LineReader lines = new(input);
        byte[]? first = lines.ReadLine();

        if (first is null)
        {
            return WorkerExitCodes.Ended;
        }

        WorkerConnection connection;

        try
        {
            connection = WorkerConnection.Read(first);
        }
        catch (FormatException failure)
        {
            // The reason quotes nothing of the line, so the password cannot reach the diagnostics, though the worker
            // does not know it yet.
            diagnostics.WriteLine("The connection line cannot be read: " + failure.Message);

            return WorkerExitCodes.BadInput;
        }

        if (_mode == WorkerModeEnum.Read && !connection.SafeMode)
        {
            diagnostics.WriteLine("The connection line turns safe mode off, and a Read worker runs only in safe mode (D12).");

            return WorkerExitCodes.BadInput;
        }

        IOneCSession session;

        try
        {
            session = _connector.Connect(connection.Location, connection.User, connection.Password);
        }
        catch (Exception failure)
        {
            WriteLine(output, ReadyLine(false));
            Report(diagnostics, connection, "No session opened: " + failure.Message);

            return WorkerExitCodes.NoSession;
        }

        using (session)
        {
            WriteLine(output, ReadyLine(true));

            return RunCommands(lines, session, connection, output, diagnostics);
        }
    }

    // Writes the text to the diagnostics with the password replaced by a placeholder, since 1C or .NET may quote a value
    // it refused. The connection string quotes a password as 1C wants, with its quotes doubled, so that form is replaced
    // too. An empty password replaces nothing.
    private static void Report(TextWriter diagnostics, WorkerConnection connection, string text)
    {
        string password = connection.Password;

        if (password.Length > 0)
        {
            text = text
                .Replace(password.Replace("\"", "\"\"", StringComparison.Ordinal), PasswordPlaceholder, StringComparison.Ordinal)
                .Replace(password, PasswordPlaceholder, StringComparison.Ordinal);
        }

        diagnostics.WriteLine(text);
    }

    // The line with its LF in one write, so that a reader sees a line whole or not at all.
    private static void WriteLine(Stream output, byte[] line)
    {
        output.Write(line);
        output.Flush();
    }

    private static byte[] ReadyLine(bool ready)
    {
        ArrayBufferWriter<byte> line = new();

        using (Utf8JsonWriter writer = new(line))
        {
            writer.WriteStartObject();
            writer.WriteBoolean("ready", ready);
            writer.WriteEndObject();
        }

        line.Write("\n"u8);

        return line.WrittenSpan.ToArray();
    }

    // An outcome the worker writes itself, in the form of the executor's line, {"size":<number>,"outcome":<object>}: the
    // outcome object of §8 of docs/protocol.md with no result, no error text and no messages, and its size in bytes.
    // The text of the failure goes to the diagnostics only.
    private static byte[] OwnOutcomeLine(string kind, string code)
    {
        ArrayBufferWriter<byte> outcome = new();

        using (Utf8JsonWriter writer = new(outcome))
        {
            writer.WriteStartObject();
            writer.WriteString("kind", kind);
            writer.WriteString("code", code);
            writer.WriteNull("result");
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

        line.Write("\n"u8);

        return line.WrittenSpan.ToArray();
    }

    // Reads and runs command lines until the input ends or a fault ends the worker, and returns the exit code.
    private int RunCommands(
        LineReader lines,
        IOneCSession session,
        WorkerConnection connection,
        Stream output,
        TextWriter diagnostics)
    {
        while (true)
        {
            byte[]? line = lines.ReadLine();

            if (line is null)
            {
                return WorkerExitCodes.Ended;
            }

            WorkerCommand command;

            try
            {
                command = WorkerCommand.Read(line);
            }
            catch (FormatException failure)
            {
                Report(diagnostics, connection, "A command line cannot be read: " + failure.Message);

                return WorkerExitCodes.BadInput;
            }

            int? exitCode = RunCommand(session, connection, command, output, diagnostics);

            if (exitCode is not null)
            {
                return exitCode.Value;
            }
        }
    }

    // Runs one command in a fresh executor and writes its outcome line. Returns the exit code when the command ends the
    // worker, and null when the worker goes on.
    private int? RunCommand(
        IOneCSession session,
        WorkerConnection connection,
        WorkerCommand command,
        Stream output,
        TextWriter diagnostics)
    {
        IOneCExecutor executor;

        try
        {
            executor = session.OpenExecutor(connection.Executor, connection.SafeMode);
        }
        catch (Exception failure)
        {
            // Nothing started, so a Write fails as a Read does.
            Report(diagnostics, connection, "The executor did not open: " + failure.Message);
            WriteLine(output, OwnOutcomeLine(Failed, BaseUnreachable));

            return WorkerExitCodes.NoSession;
        }

        using (executor)
        {
            string text;

            try
            {
                text = executor.Run(ModeText, command.Script, command.ParametersJson, command.CallerKey, command.OutcomeLimit);
            }
            catch (Exception failure)
            {
                // The executor itself never raises, so the session ended while the script ran, and a Write may have
                // committed.
                Report(diagnostics, connection, "The session ended while the script ran: " + failure.Message);
                WriteLine(output, OwnOutcomeLine(FailedOrUnknown, ConnectionLost));

                return WorkerExitCodes.SessionLost;
            }

            // A line break would cut the text into two lines and shift every later outcome. The session is whole, so the
            // worker goes on.
            if (text.AsSpan().IndexOfAny('\n', '\r') >= 0)
            {
                Report(diagnostics, connection, "The executor returned a text with a line break, which one line cannot carry.");
                WriteLine(output, OwnOutcomeLine(FailedOrUnknown, ResultType));

                return null;
            }

            // An outcome line is never changed: the text is valid Unicode, so its UTF-8 bytes are those the executor
            // measured (§8 of docs/protocol.md).
            WriteLine(output, Encoding.UTF8.GetBytes(text + "\n"));

            return null;
        }
    }
}
