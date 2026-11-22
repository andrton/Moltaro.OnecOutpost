namespace Moltaro.OnecOutpost.Agent.Tests.FakeHttp;

// The body of an answer whose status and headers came and whose bytes stop: it gives its first bytes, and then a read
// waits until its token is cancelled, as a hop that holds an answer back does. A test of a wait ends it with the fake
// clock, once Stalled shows that the reader has the status and waits for the rest of the body.
internal sealed class StalledStream : Stream
{
    private readonly byte[] _first;

    // Completed apart, so that the test never goes on inside the reader's call.
    private readonly TaskCompletionSource _stalled = new(TaskCreationOptions.RunContinuationsAsynchronously);

    private int _position;

    public StalledStream(byte[] first)
    {
        _first = first;
    }

    // Completes once a read waits for the bytes after the first: the answer began, and its body stopped.
    public Task Stalled => _stalled.Task;

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
        throw new NotSupportedException("The agent reads an answer asynchronously, with the token of its wait.");
    }

    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        if (_position < _first.Length)
        {
            int count = Math.Min(buffer.Length, _first.Length - _position);
            _first.AsMemory(_position, count).CopyTo(buffer);
            _position += count;

            return count;
        }

        _stalled.TrySetResult();
        await Task.Delay(Timeout.Infinite, cancellationToken);

        return 0;
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
