namespace Moltaro.OnecOutpost.TestSupport.FakeHttp;

// The body of an answer that is made as it is read, Length bytes of white space, so that a test of the read limits
// (§10 of docs/protocol.md) sends more bytes than a limit without holding them. It counts what was read of it, so that a
// test states that a body declared beyond its limit is lost unread. It cannot seek, so a content made of it declares no
// length unless the test declares one.
internal sealed class GeneratedStream : Stream
{
    private long _position;

    public GeneratedStream(long length)
    {
        Length = length;
    }

    public override long Length { get; }

    // The bytes read so far. Written and read by one reader at a time.
    public long BytesRead => Interlocked.Read(ref _position);

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

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
        long left = Length - _position;
        int count = (int)Math.Min(buffer.Length, left);

        buffer[..count].Fill((byte)' ');
        Interlocked.Add(ref _position, count);

        return count;
    }

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return ValueTask.FromResult(Read(buffer.Span));
    }

    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
    {
        return ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();
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
