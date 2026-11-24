using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Moltaro.OnecOutpost.Protocol.Signing;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Programs;

namespace Moltaro.OnecOutpost.Shell.Tests.Sessions;

// A child shell spoken to over its standard streams. The tests wait for its output to continue a live session and
// close its input to end it. Standard error is drained even while a test waits for a line on standard output.
internal sealed class ShellSession : IAsyncDisposable
{
    private const string AssemblyName = "Moltaro.OnecOutpost.Shell.dll";

    private const string TokenPattern = @"outpost1\.[0-9a-f-]{36}\.[A-Za-z0-9_-]{22}\.[0-9a-f]{64}";

    public const string PostgresVariable = "OUTPOST_SHELL_POSTGRES";
    public const string CertificatePasswordVariable = "OUTPOST_SHELL_CERTIFICATE_PASSWORD";
    public const string LoopbackOrigin = "http://127.0.0.1:0";

    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Process _process;
    private readonly Task<string> _error;
    private readonly StringBuilder _output = new();

    private ShellSession(Process process)
    {
        _process = process;
        _error = process.StandardError.ReadToEndAsync();
    }

    public int ProcessId => _process.Id;

    public static ShellSession Start(string? connection, params string[] arguments)
    {
        return StartDotnet(connection, null, [Path.Combine(AppContext.BaseDirectory, AssemblyName), .. arguments]);
    }

    public static ShellSession Start(
        string? connection,
        IReadOnlyDictionary<string, string> variables,
        params string[] arguments)
    {
        return StartDotnet(connection, null, [Path.Combine(AppContext.BaseDirectory, AssemblyName), .. arguments], variables);
    }

    public static ShellSession StartDotnet(
        string? connection,
        string? workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string>? variables = null)
    {
        ProcessStartInfo start = new(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardInputEncoding = Utf8,
            StandardOutputEncoding = Utf8,
            StandardErrorEncoding = Utf8,
            UseShellExecute = false,
        };

        if (workingDirectory is not null)
        {
            start.WorkingDirectory = workingDirectory;
        }

        start.Environment.Remove(PostgresVariable);
        start.Environment.Remove(CertificatePasswordVariable);

        if (connection is not null)
        {
            start.Environment[PostgresVariable] = connection;
        }

        if (variables is not null)
        {
            foreach ((string name, string value) in variables)
            {
                start.Environment[name] = value;
            }
        }

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        return new ShellSession(Process.Start(start) ?? throw new InvalidOperationException("The shell did not start."));
    }

    public async Task<Uri> AddressAsync()
    {
        const string AddressPattern = @"https?://[^\s]+/outpost/?(?=\s|$)";
        string output = await ReadThroughAsync(line => Regex.IsMatch(line, AddressPattern));

        return new Uri(Regex.Match(output, AddressPattern).Value);
    }

    public async Task<string> TokenAsync()
    {
        string output = await ReadThroughAsync(line => Regex.IsMatch(line, TokenPattern));
        string token = Regex.Match(output, TokenPattern).Value;
        Assert.True(OutpostPairingTokenText.TryRead(token, out _));

        return token;
    }

    public Task SendAsync(string input)
    {
        return SendAsync(Utf8.GetBytes(input));
    }

    public async Task SendAsync(ReadOnlyMemory<byte> input)
    {
        using CancellationTokenSource deadline = Deadline();
        await _process.StandardInput.BaseStream.WriteAsync(input, deadline.Token);
        await _process.StandardInput.BaseStream.FlushAsync(deadline.Token);
    }

    public async Task<string> ReadThroughAsync(Func<string, bool> last)
    {
        using CancellationTokenSource deadline = Deadline();
        StringBuilder block = new();

        while (await _process.StandardOutput.ReadLineAsync(deadline.Token) is string line)
        {
            _output.AppendLine(line);
            block.AppendLine(line);

            if (last(line))
            {
                return block.ToString();
            }
        }

        string diagnostics = _output + await _error.WaitAsync(deadline.Token);
        throw new InvalidOperationException(
            "The shell ended before it wrote the expected line.\n" + Regex.Replace(diagnostics, TokenPattern, "<pairing token>"));
    }

    public async Task<ProgramEnd> EndAsync(bool closeInput = true)
    {
        if (closeInput)
        {
            _process.StandardInput.Close();
        }

        using CancellationTokenSource deadline = Deadline();
        Task<string> remaining = _process.StandardOutput.ReadToEndAsync(deadline.Token);

        await _process.WaitForExitAsync(deadline.Token);
        _output.Append(await remaining);

        return new ProgramEnd(_process.ExitCode, _output.ToString(), await _error.WaitAsync(deadline.Token));
    }

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            _process.Kill(entireProcessTree: true);
            await _process.WaitForExitAsync().WaitAsync(TestTimes.Deadline);
        }

        _process.Dispose();
    }

    private static CancellationTokenSource Deadline()
    {
        CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestTimes.Deadline);

        return deadline;
    }
}
