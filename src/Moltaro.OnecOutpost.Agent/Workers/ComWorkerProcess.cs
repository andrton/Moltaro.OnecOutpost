using System.Buffers;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.Protocol.Codec;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Workers;

// One COM worker process of the program's launcher, or of the console's check and metadata, speaking the worker's lines over its raw
// standard streams, as the lab's WorkerProcess does: one JSON object per line in UTF-8, ended with LF, each line read as
// bytes up to its LF. A failed write gives false or null; cancellation kills and throws OperationCanceledException.
// Every open or run that does not give true or an outcome has killed the process. Limitation: the agent keeps no
// diagnostics of a worker; the console's check shows why one does not open.
//
// The pool calls OpenAsync and then RunAsync one at a time; Kill may run beside either, from any thread. A read from
// the pipe of a process does not end on cancellation, so cancellation kills the process, which ends its output.
// Limitation: Windows gives anonymous pipes no asynchronous I/O, so each open or run holds a pool thread while it waits.
// It owns its streams, the handle of its process, and the job of its own when it is given one. Start is the one start
// of a worker's process, for the launcher and the console.
[SupportedOSPlatform("windows")]
internal sealed partial class ComWorkerProcess : IWorkerProcess
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

    private readonly SafeProcessHandle _process;

    private readonly Stream _input;

    private readonly Stream _output;

    // The job of its own that a worker of the console's check is in, which Dispose closes last; null for a worker of the
    // agent, which is in the launcher's job.
    private readonly WorkerJob? _job;

    // 1 once the first Kill ended the process: Kill is idempotent, and only the first one calls TerminateProcess.
    private int _killed;

    // The process that the launcher created, which it puts into the job before the process runs, with the agent's ends
    // of its standard input and output; or one that the console's check created, with the job of its own, which it then
    // owns.
    public ComWorkerProcess(SafeProcessHandle process, Stream input, Stream output, WorkerJob? job = null)
    {
        _process = process;
        _input = input;
        _output = output;
        _job = job;
    }

    // Starts the program as a worker of the base in the mode: the command line CommandLineOf gives, two pipes of its own
    // for the standard input and output, the error end when given, WorkerCreation.CreateSuspended with the call, the
    // token and the desktop; then one statement, run whether the process was made or not, closes the caller's copies of
    // the client ends given to it, so that the process holds the only ones and none waits for the finalizer; then
    // WorkerCreation.JoinAndResume into the job, so that the worker is in it before any of its code runs. The worker owns
    // the job when ownsJob, and the caller keeps the error pipe. A failure is the Win32Exception of a call, or the
    // IOException of a pipe that Windows could not make, after which its pipes and any process it made are disposed and
    // the job stays the caller's; any other exception, such as the ObjectDisposedException of a disposed job, passes
    // the same way. The connection, with its password, travels only in the first input line, never here.
    public static ComWorkerProcess Start(
        WorkerCallEnum call,
        SafeAccessTokenHandle token,
        string program,
        string baseKey,
        OutpostModeEnum mode,
        string desktop,
        WorkerJob job,
        bool ownsJob,
        AnonymousPipeServerStream? error)
    {
        AnonymousPipeServerStream? input = null;
        AnonymousPipeServerStream? output = null;
        SafeProcessHandle? process = null;

        try
        {
            input = new AnonymousPipeServerStream(PipeDirection.Out, HandleInheritability.Inheritable);
            output = new AnonymousPipeServerStream(PipeDirection.In, HandleInheritability.Inheritable);
            nint thread;

            try
            {
                (process, thread) = WorkerCreation.CreateSuspended(
                    call,
                    token,
                    program,
                    CommandLineOf(program, baseKey, mode),
                    desktop,
                    input.ClientSafePipeHandle,
                    output.ClientSafePipeHandle,
                    error?.ClientSafePipeHandle);
            }
            finally
            {
                // A pipe's dispose leaves its client end open once it was handed out, so the copies close here.
                CloseClientCopies(input, output, error);
            }

            WorkerCreation.JoinAndResume(job, process, thread);

            return new ComWorkerProcess(process, input, output, ownsJob ? job : null);
        }
        catch
        {
            process?.Dispose();
            input?.Dispose();
            output?.Dispose();
            throw;
        }
    }

    // "{program}" {base key} {read|write}, the one command line of a worker, which the console's probe uses too.
    public static string CommandLineOf(string program, string baseKey, OutpostModeEnum mode)
    {
        return $"\"{program}\" {baseKey} {OutpostForms.WriteMode(mode)}";
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

    // Ends the console's session by closing the input, then waits until the worker has released the base and exited.
    public async Task EndAsync(TimeSpan deadline, TimeProvider clock)
    {
        CloseInput();
        await WorkerCreation.ExitCodeWithinAsync(_process, deadline, clock, CancellationToken.None);
    }

    // TerminateProcess. Its failure is ignored: the handle of a started process may terminate it, so the call fails
    // only for a process that has ended, as Process.Kill ignores one.
    public void Kill()
    {
        if (Interlocked.Exchange(ref _killed, 1) == 1)
        {
            return;
        }

        WorkerCreation.Terminate(_process);
    }

    // Closes the worker's input, which ends a worker that still runs at the end of its input, and its output, then
    // releases the process, and closes its own job, which ends whatever of it still runs. The pool disposes a worker
    // only after it killed it and its open and run ended.
    public void Dispose()
    {
        CloseInput();
        _output.Dispose();
        _process.Dispose();
        _job?.Dispose();
    }

    private void CloseInput()
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
    }

    // The caller's copies of the client ends of the pipes, in one call.
    private static void CloseClientCopies(params AnonymousPipeServerStream?[] pipes)
    {
        foreach (AnonymousPipeServerStream? pipe in pipes)
        {
            pipe?.DisposeLocalCopyOfClientHandle();
        }
    }

    // The connection line of the worker: location, user, password, safeMode and executor. The password travels only
    // here.
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

    // The command line of the worker: script, parameters, callerKey and outcomeLimit. The parameters are the text of
    // the signed command, which the codec read as JSON; a writer of §2 wrote them without white space, so they hold no
    // LF.
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
    // which the COM worker never writes before it reads its next line.
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
