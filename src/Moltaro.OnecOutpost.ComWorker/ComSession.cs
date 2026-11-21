using System.Runtime.Versioning;

namespace Moltaro.OnecOutpost.ComWorker;

// The session of ComConnector in one base: the external connection and the COM connector that made it, which it owns
// and releases once when it is disposed. The session in the base ends when nothing holds the connection any more.
[SupportedOSPlatform("windows")]
internal sealed class ComSession : IOneCSession
{
    private readonly object _connector;
    private readonly object _connection;
    private bool _disposed;

    public ComSession(object connector, object connection)
    {
        _connector = connector;
        _connection = connection;
    }

    // Opens the executor with ExternalDataProcessors.Create, as the lab check L1 opened it. The objects of 1C that the
    // call needs are released before it returns; the executor belongs to the ComExecutor returned.
    public IOneCExecutor OpenExecutor(string path, bool safeMode)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        object processors = ComDispatch.Get(_connection, "ExternalDataProcessors")
            ?? throw new InvalidOperationException("The connection gave no ExternalDataProcessors.");

        try
        {
            object protection = ComDispatch.Invoke(_connection, "NewObject", "UnsafeOperationProtectionDescription")
                ?? throw new InvalidOperationException("The connection gave no UnsafeOperationProtectionDescription.");

            try
            {
                // Nobody can answer the platform's warning about opening a file in an external connection, so the
                // warning is off for this call (concept §12, O2).
                ComDispatch.Set(protection, "UnsafeOperationWarnings", false);

                object executor = ComDispatch.Invoke(processors, "Create", path, safeMode, protection)
                    ?? throw new InvalidOperationException("ExternalDataProcessors.Create gave no object.");

                return new ComExecutor(executor);
            }
            finally
            {
                ComDispatch.Release(protection);
            }
        }
        finally
        {
            ComDispatch.Release(processors);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        // The connection first, since the connector made it.
        ComDispatch.Release(_connection);
        ComDispatch.Release(_connector);
    }
}
