using System.Buffers;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.OneC.Tests.Lab;

namespace Moltaro.OnecOutpost.OneC.Tests.Workers;

// The worker program started as a process and spoken to over its raw standard streams, as the agent will: the
// connection line first and {"ready":...} back, then one command line at a time, each answered by one outcome line,
// read as bytes up to its LF. The lines go out as JSON in UTF-8 without a BOM, ended with LF. The standard error is
// collected for the message of a failed assertion; the worker writes no password there. Disposing closes the input of a
// worker that still runs, as the agent ends one, and kills it only if it does not end.
internal sealed class WorkerProcess : IAsyncDisposable
{
    // The outcome limit of a command when a test gives none: the default of the server, 16 MiB (concept rule 5).
    public const long DefaultOutcomeLimit = 16 * 1024 * 1024;

    // How long a test waits for one line of the worker, or for its end. The first connection of a Windows account that
    // never used 1C took twenty seconds on the lab machine (concept §12).
    private static readonly TimeSpan Deadline = TimeSpan.FromMinutes(2);

    private readonly Process _process;
    private readonly Stream _input;
    private readonly Stream _output;

    // Appended by the thread that reads the standard error, read by the test: both under the lock of the builder.
    private readonly StringBuilder _diagnostics = new();

    // The bytes of the standard output that were read and not yet returned as a line: from _start to _end.
    private readonly byte[] _buffer = new byte[64 * 1024];
    private int _start;
    private int _end;

    private WorkerProcess(Process process)
    {
        _process = process;
        _input = process.StandardInput.BaseStream;
        _output = process.StandardOutput.BaseStream;
    }

    public string Diagnostics
    {
        get
        {
            lock (_diagnostics)
            {
                return _diagnostics.ToString();
            }
        }
    }

    // Starts the worker program with the command line given, normally a base key and a mode.
    public static WorkerProcess Start(params string[] arguments)
    {
        ProcessStartInfo start = new(Repository.WorkerPath)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process = Process.Start(start) ?? throw new InvalidOperationException("The worker program did not start.");
        WorkerProcess worker = new(process);

        process.ErrorDataReceived += (_, line) => worker.AddDiagnostics(line.Data);
        process.BeginErrorReadLine();

        return worker;
    }

    // Writes the connection line and reads {"ready":...}: true when the session opened.
    public async Task<bool> ConnectAsync(string location, string user, string password, bool safeMode, string executor)
    {
        await WriteLineAsync(writer =>
        {
            writer.WriteString("location", location);
            writer.WriteString("user", user);
            writer.WriteString("password", password);
            writer.WriteBoolean("safeMode", safeMode);
            writer.WriteString("executor", executor);
        });

        byte[] line = await ReadLineAsync() ?? throw new InvalidOperationException(
            "The worker ended without {\"ready\":...}. " + Diagnostics);

        using JsonDocument ready = JsonDocument.Parse(line);

        return ready.RootElement.GetProperty("ready").GetBoolean();
    }

    // Writes a command line and reads its outcome line. Every outcome is held to the check the agent makes (§8 of
    // docs/protocol.md): the size the executor measured is the length of the bytes of the outcome object received.
    public async Task<OutcomeLine> RunAsync(
        string script,
        string parameters = "{}",
        string? callerKey = null,
        long outcomeLimit = DefaultOutcomeLimit)
    {
        await WriteLineAsync(writer =>
        {
            writer.WriteString("script", script);
            writer.WritePropertyName("parameters");
            writer.WriteRawValue(parameters);

            if (callerKey is null)
            {
                writer.WriteNull("callerKey");
            }
            else
            {
                writer.WriteString("callerKey", callerKey);
            }

            writer.WriteNumber("outcomeLimit", outcomeLimit);
        });

        byte[] line = await ReadLineAsync() ?? throw new InvalidOperationException(
            "The worker ended without an outcome. " + Diagnostics);

        OutcomeLine outcome = OutcomeLine.Read(line);
        Assert.Equal(outcome.OutcomeBytes.Length, outcome.Size);

        return outcome;
    }

    // Reads the next line of the standard output without its LF, or null when the output ended after a whole line.
    public async Task<byte[]?> ReadLineAsync()
    {
        using CancellationTokenSource deadline = StartDeadline();

        // A read from the pipe of a process does not end on cancellation, so the deadline ends the worker, which ends
        // its output.
        using CancellationTokenRegistration end = deadline.Token.Register(EndProcess);

        ArrayBufferWriter<byte> line = new();

        while (true)
        {
            int lineFeed = _buffer.AsSpan(_start, _end - _start).IndexOf((byte)'\n');

            if (lineFeed >= 0)
            {
                line.Write(_buffer.AsSpan(_start, lineFeed));
                _start += lineFeed + 1;

                return line.WrittenSpan.ToArray();
            }

            line.Write(_buffer.AsSpan(_start, _end - _start));
            _start = 0;
            _end = await _output.ReadAsync(_buffer);

            if (_end == 0)
            {
                Assert.False(deadline.IsCancellationRequested, $"The worker wrote no line within {Deadline}. {Diagnostics}");
                Assert.True(line.WrittenCount == 0, "The worker ended its output inside a line.");

                return null;
            }
        }
    }

    // Closes the standard input, as the agent does to end a worker, and returns its exit code.
    public async Task<int> EndAsync()
    {
        _input.Close();

        return await WaitForExitAsync();
    }

    // Waits for a worker that ends by itself and returns its exit code.
    public async Task<int> WaitForExitAsync()
    {
        using CancellationTokenSource deadline = StartDeadline();

        await _process.WaitForExitAsync(deadline.Token);

        return _process.ExitCode;
    }

    // Ends the worker as the agent does, by closing its input, so that it releases its session in 1C and ends. It is
    // killed only when it does not end within the deadline, as one whose script still runs.
    public async ValueTask DisposeAsync()
    {
        try
        {
            if (!_process.HasExited)
            {
                await EndAsync();
            }
        }
        catch (OperationCanceledException)
        {
            // The deadline passed; the worker is killed below.
        }
        finally
        {
            EndProcess();
            await _process.WaitForExitAsync();
            _process.Dispose();
        }
    }

    private static CancellationTokenSource StartDeadline()
    {
        CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken);

        deadline.CancelAfter(Deadline);

        return deadline;
    }

    // Writes one JSON object, whose members the caller writes, as one line.
    private async Task WriteLineAsync(Action<Utf8JsonWriter> writeMembers)
    {
        ArrayBufferWriter<byte> line = new();

        using (Utf8JsonWriter writer = new(line))
        {
            writer.WriteStartObject();
            writeMembers(writer);
            writer.WriteEndObject();
        }

        line.Write("\n"u8);

        await _input.WriteAsync(line.WrittenMemory, TestContext.Current.CancellationToken);
        await _input.FlushAsync(TestContext.Current.CancellationToken);
    }

    private void AddDiagnostics(string? line)
    {
        if (line is null)
        {
            return;
        }

        lock (_diagnostics)
        {
            _diagnostics.AppendLine(line);
        }
    }

    private void EndProcess()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
        }
    }
}
