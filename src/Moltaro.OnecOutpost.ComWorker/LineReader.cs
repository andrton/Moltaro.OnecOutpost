using System.Buffers;

namespace Moltaro.OnecOutpost.ComWorker;

// Reads the worker's input as lines of bytes, each up to its LF. It reads from the input only when the bytes it holds
// have no LF, so it never asks for a line before the worker has answered the one before it: a pipe holds no more than
// the agent wrote. A last line without its LF is a line. The bytes are not decoded here; LineJson reads them as UTF-8.
internal sealed class LineReader
{
    private readonly Stream _input;

    // The bytes read and not yet returned as a line: from _start to _end.
    private readonly byte[] _buffer = new byte[64 * 1024];
    private int _start;
    private int _end;

    public LineReader(Stream input)
    {
        _input = input;
    }

    // Returns the next line without its LF, or null when the input ended after a whole line or before any.
    public byte[]? ReadLine()
    {
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
            _end = _input.Read(_buffer, 0, _buffer.Length);

            if (_end == 0)
            {
                return line.WrittenCount == 0 ? null : line.WrittenSpan.ToArray();
            }
        }
    }
}
