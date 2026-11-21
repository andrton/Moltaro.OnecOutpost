namespace Moltaro.OnecOutpost.ComWorker.Tests;

// The standard input of a worker, written as the agent writes it: the connection line, then each command line only
// after the worker answered the line before it, since the agent waits for {"ready":...} and then for the outcome of
// each command before it sends the next. A read never returns bytes of two lines, as a pipe holds no more than the
// agent wrote. After the last line a read returns the end of the input at once, as when the agent closes it.
//
// The worker runs on the thread of the test, so a line that the agent would not have sent yet cannot be held back
// without a deadlock. It is delivered anyway, and ReadAhead records that the worker took it too early.
internal sealed class AgentInput : Stream
{
    private readonly byte[][] _lines;

    // Counts the lines the worker answered.
    private readonly WorkerOutput _output;

    // The line being delivered, and how many of its bytes the worker took.
    private int _line;
    private int _offset;

    // Each line is given with its LF, but the last may lack it.
    public AgentInput(WorkerOutput output, params byte[][] lines)
    {
        _output = output;
        _lines = lines;
    }

    // Whether the worker took a line before it had answered every line before it.
    public bool ReadAhead { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        return Read(buffer.AsSpan(offset, count));
    }

    public override int Read(Span<byte> buffer)
    {
        if (buffer.IsEmpty || _line == _lines.Length)
        {
            return 0;
        }

        // Line k is sent once the worker answered the k lines before it: the connection line with {"ready":...}, and
        // each command line with its outcome.
        if (_offset == 0 && _output.LineCount < _line)
        {
            ReadAhead = true;
        }

        byte[] line = _lines[_line];
        int count = Math.Min(buffer.Length, line.Length - _offset);
        line.AsSpan(_offset, count).CopyTo(buffer);
        _offset += count;

        if (_offset == line.Length)
        {
            _line++;
            _offset = 0;
        }

        return count;
    }

    public override void Flush()
    {
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }
}
