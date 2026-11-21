using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.ComWorker;

// One object of the external data processor OutpostExecutor, opened by ComSession, which this owns and releases once
// when it is disposed.
[SupportedOSPlatform("windows")]
internal sealed class ComExecutor : IOneCExecutor
{
    private readonly object _executor;
    private bool _disposed;

    public ComExecutor(object executor)
    {
        _executor = executor;
    }

    // A null caller key goes as VT_EMPTY, which 1C reads as Undefined. The outcome limit goes as a Double, a Number in
    // 1C, which holds every limit of the protocol exactly.
    public string Run(string mode, string script, string parametersJson, string? callerKey, long outcomeLimit)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        object? text = ComDispatch.Invoke(_executor, "Run", mode, script, parametersJson, callerKey, (double)outcomeLimit);

        // The executor returns a text for every command; anything else is not the executor of src/Executor.
        return text as string ?? throw new InvalidOperationException("The executor's Run returned no text.");
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        ComDispatch.Release(_executor);
    }
}
