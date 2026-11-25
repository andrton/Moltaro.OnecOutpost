using System.Buffers;
using System.Text;

namespace Moltaro.OnecOutpost.Shell;

// Decode one line at a time. A bad byte on a later line must not prevent an earlier command from being run.
internal sealed class SessionInput
{
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    private readonly Stream? _input;
    private readonly byte[] _buffer = new byte[4096];
    private int _start;
    private int _end;
    private bool _firstLine = true;

    public SessionInput()
    {
        if (OperatingSystem.IsWindows() && !Console.IsInputRedirected)
        {
            // A Windows terminal provides Unicode characters through ReadConsoleW. Redirected input is UTF-8 bytes.
            Console.InputEncoding = Encoding.Unicode;
        }
        else
        {
            _input = Console.OpenStandardInput();
        }
    }

    public bool EndOfInput { get; private set; }

    public async Task<string?> ReadLineAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (EndOfInput)
        {
            return null;
        }

        string? line;
        if (_input is null)
        {
            line = await Task.Run(Console.ReadLine, cancellationToken).WaitAsync(cancellationToken);
            if (line is null or "\u001a")
            {
                EndOfInput = true;
                return null;
            }
        }
        else
        {
            line = await ReadUtf8LineAsync(_input, cancellationToken);
        }

        if (_firstLine)
        {
            _firstLine = false;
            if (line is not null && line.StartsWith('\uFEFF'))
            {
                line = line[1..];
            }
        }

        return line;
    }

    private async Task<string?> ReadUtf8LineAsync(Stream input, CancellationToken cancellationToken)
    {
        ArrayBufferWriter<byte>? earlier = null;

        while (true)
        {
            ReadOnlySpan<byte> buffered = _buffer.AsSpan(_start, _end - _start);
            int newline = buffered.IndexOf((byte)'\n');
            if (newline >= 0)
            {
                _start += newline + 1;
                ReadOnlySpan<byte> line = buffered[..newline];
                if (earlier is not null)
                {
                    earlier.Write(line);
                    line = earlier.WrittenSpan;
                }

                if (!line.IsEmpty && line[^1] == (byte)'\r')
                {
                    line = line[..^1];
                }

                return Utf8.GetString(line);
            }

            if (!buffered.IsEmpty)
            {
                earlier ??= new ArrayBufferWriter<byte>();
                earlier.Write(buffered);
            }

            _start = 0;
            // An OS read of a terminal or pipe can remain blocked after Ctrl+C. The session's wait ends either way.
            _end = await input.ReadAsync(_buffer.AsMemory(), cancellationToken).AsTask().WaitAsync(cancellationToken);
            if (_end == 0)
            {
                EndOfInput = true;
                return earlier is null ? null : Utf8.GetString(earlier.WrittenSpan);
            }
        }
    }
}
