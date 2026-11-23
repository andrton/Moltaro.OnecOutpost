using System.Buffers;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;

namespace Moltaro.OnecOutpost.Agent.Workers;

// One COM worker process of the program's launcher, speaking S9's lines over its raw standard streams, as S9's lab
// helper does: one JSON object per line in UTF-8, ended with LF, each line read as bytes up to its LF. A failed write
// gives false or null; cancellation kills and throws OperationCanceledException. Every open or run that does not give
// true or an outcome has killed the process. Limitation: the agent keeps no diagnostics of a worker; the console's check
// shows why one does not open (S10).
//
// The pool calls OpenAsync and then RunAsync one at a time; Kill may run beside either, from any thread. A read from
// the pipe of a process does not end on cancellation, so cancellation kills the process, which ends its output.
// Limitation: .NET 10 opens the redirected pipes for synchronous I/O, so each open or run holds a pool thread while it
// waits.
[SupportedOSPlatform("windows")]
internal sealed class ComWorkerProcess : IWorkerProcess
{
    // The longest ready line, its LF not counted.
    private const int ReadyLimit = 1024;

    // What an outcome line may add to the outcome it carries: {"size":, the size, ,"outcome": and }.
    private const int OutcomeLineMargin = 64;

    // The bytes asked of the standard output at a time.
    private const int ReadSize = 64 * 1024;

    private static readonly JsonDocumentOptions OutcomeLineOptions = new()
    {
        MaxDepth = OutpostProtocol.MaxMessageDepth,
    };

    private readonly Process _process;

    private readonly Stream _input;

    private readonly Stream _output;

    // 1 once the first Kill ended the process: Kill is idempotent, and only the first one calls Process.Kill.
    private int _killed;

    // The process the launcher started, with its standard input and output redirected, already in the job.
    public ComWorkerProcess(Process process)
    {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
    }

    // Writes the connection line and reads one line of at most 1 KiB: true only for an object whose one member is
    // "ready":true; anything else, the end of the output included, kills the process and gives false.
    public async Task<bool> OpenAsync(WorkerConnectionLine connection, CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration end = cancellationToken.Register(Kill);
        cancellationToken.ThrowIfCancellationRequested();

        byte[]? line = await ExchangeAsync(ConnectionLine(connection), ReadyLimit);

        cancellationToken.ThrowIfCancellationRequested();

        if (line is not null && IsReady(line))
        {
            return true;
        }

        Kill();

        return false;
    }

    // Writes the command line, ParametersJson as it is, and reads one line of at most OutcomeLimit plus 64 bytes. An
    // object with exactly size, an integer from 0, then outcome, any JSON value, read to
    // OutpostProtocol.MaxMessageDepth, gives size and the exact bytes of outcome. The end of the output before a whole
    // line, a longer line and any other line kill the process and give null.
    public async Task<(long Size, byte[] Outcome)?> RunAsync(
        WorkerCommandLine command,
        CancellationToken cancellationToken)
    {
        using CancellationTokenRegistration end = cancellationToken.Register(Kill);
        cancellationToken.ThrowIfCancellationRequested();

        byte[]? line = await ExchangeAsync(CommandLine(command), command.OutcomeLimit + OutcomeLineMargin);

        cancellationToken.ThrowIfCancellationRequested();

        if (line is not null && ReadOutcomeLine(line) is { } given)
        {
            return given;
        }

        Kill();

        return null;
    }

    // Process.Kill, an ended process ignored, as Process.Kill ignores one.
    public void Kill()
    {
        if (Interlocked.Exchange(ref _killed, 1) == 1)
        {
            return;
        }

        _process.Kill();
    }

    // Closes the worker's input, which ends a worker that still runs at the end of its input, and its output, then
    // releases the process. The pool disposes a worker only after it killed it and its open and run ended.
    public void Dispose()
    {
        try
        {
            _input.Dispose();
        }
        catch (IOException)
        {
            // A write that failed left its bytes in the buffer of the stream, which its dispose writes again into a
            // pipe whose worker has ended. The handle is closed all the same.
        }

        _output.Dispose();
        _process.Dispose();
    }

    // The connection line of S9: location, user, password, safeMode and executor. The password travels only here.
    private static byte[] ConnectionLine(WorkerConnectionLine connection)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("location");
        writer.String(connection.Location);
        writer.Name("user");
        writer.String(connection.User);
        writer.Name("password");
        writer.String(connection.Password);
        writer.Name("safeMode");
        writer.Boolean(connection.SafeMode);
        writer.Name("executor");
        writer.String(connection.Executor);
        writer.EndObject();

        return WithLineFeed(writer);
    }

    // The command line of S9: script, parameters, callerKey and outcomeLimit. The parameters are the text of the signed
    // command, which the codec read as JSON; a writer of §2 wrote them without white space, so they hold no LF.
    private static byte[] CommandLine(WorkerCommandLine command)
    {
        OutpostJsonWriter writer = new();

        writer.StartObject();
        writer.Name("script");
        writer.String(command.Script);
        writer.Name("parameters");
        writer.Raw(Encoding.UTF8.GetBytes(command.ParametersJson));
        writer.Name("callerKey");
        writer.NullableString(command.CallerKey);
        writer.Name("outcomeLimit");
        writer.Integer(command.OutcomeLimit);
        writer.EndObject();

        return WithLineFeed(writer);
    }

    private static byte[] WithLineFeed(OutpostJsonWriter writer)
    {
        byte[] json = writer.ToArray();
        byte[] line = new byte[json.Length + 1];

        json.CopyTo(line, 0);
        line[^1] = (byte)'\n';

        return line;
    }

    // True for an object whose one member is "ready":true.
    private static bool IsReady(byte[] line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line);
            JsonElement root = document.RootElement;

            return root.ValueKind == JsonValueKind.Object
                && root.EnumerateObject().ToArray() is [{ Value.ValueKind: JsonValueKind.True } ready]
                && ready.NameEquals("ready"u8);
        }
        catch (JsonException)
        {
            return false;
        }
    }

    // The size and the exact bytes of the outcome of an object with exactly size, an integer from 0, then outcome, any
    // JSON value; null for any other line.
    private static (long Size, byte[] Outcome)? ReadOutcomeLine(byte[] line)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(line, OutcomeLineOptions);
            JsonElement root = document.RootElement;

            if (root.ValueKind != JsonValueKind.Object
                || root.EnumerateObject().ToArray() is not [JsonProperty size, JsonProperty outcome]
                || !size.NameEquals("size"u8)
                || !outcome.NameEquals("outcome"u8)
                || size.Value.ValueKind != JsonValueKind.Number
                || !size.Value.TryGetInt64(out long measured)
                || measured < 0)
            {
                return null;
            }

            return (measured, JsonMarshal.GetRawUtf8Value(outcome.Value).ToArray());
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Writes one line and reads the one line of the answer, of at most limit bytes, its LF not counted; null when the
    // write fails, when the output ends before a whole line, when the line is longer, and when bytes follow its LF,
    // which S9's worker never writes before it reads its next line.
    private async Task<byte[]?> ExchangeAsync(byte[] line, long limit)
    {
        try
        {
            await _input.WriteAsync(line);
            await _input.FlushAsync();

            return await ReadLineAsync(limit);
        }
        catch (IOException)
        {
            // The pipe of a worker that ended, or that a kill ends.
            return null;
        }
    }

    private async Task<byte[]?> ReadLineAsync(long limit)
    {
        ArrayBufferWriter<byte> line = new();

        while (true)
        {
            Memory<byte> buffer = line.GetMemory(ReadSize);
            int read = await _output.ReadAsync(buffer);

            if (read == 0)
            {
                return null;
            }

            int lineFeed = buffer.Span[..read].IndexOf((byte)'\n');

            if (lineFeed >= 0)
            {
                bool whole = lineFeed == read - 1 && line.WrittenCount + lineFeed <= limit;

                line.Advance(lineFeed);

                return whole ? line.WrittenSpan.ToArray() : null;
            }

            line.Advance(read);

            if (line.WrittenCount > limit)
            {
                return null;
            }
        }
    }
}
