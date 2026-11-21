using System.Text;

namespace Moltaro.OnecOutpost.ComWorker.Tests;

// The standard output of a worker as the agent reads it. Every write is kept as it came, so that a test can see which
// bytes went out in one write, and the lines are counted as they arrive, so that AgentInput can tell whether the worker
// answered a line before it takes the next.
internal sealed class WorkerOutput : Stream
{
    private readonly List<byte[]> _writes = [];

    public IReadOnlyList<byte[]> Writes => _writes;

    public byte[] Bytes => [.. _writes.SelectMany(write => write)];

    public string Text => Encoding.UTF8.GetString(Bytes);

    // The number of lines the worker ended with LF so far.
    public int LineCount => _writes.Sum(write => write.Count(value => value == (byte)'\n'));

    // The lines the worker ended with LF, each without its LF. Bytes after the last LF are not a line.
    public IReadOnlyList<byte[]> Lines
    {
        get
        {
            List<byte[]> lines = [];
            byte[] bytes = Bytes;
            int start = 0;

            for (int index = 0; index < bytes.Length; index++)
            {
                if (bytes[index] == (byte)'\n')
                {
                    lines.Add(bytes[start..index]);
                    start = index + 1;
                }
            }

            return lines;
        }
    }

    public override bool CanRead => false;

    public override bool CanSeek => false;

    public override bool CanWrite => true;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        Write(buffer.AsSpan(offset, count));
    }

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        _writes.Add(buffer.ToArray());
    }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }
}
