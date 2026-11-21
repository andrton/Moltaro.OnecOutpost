namespace Moltaro.OnecOutpost.ComWorker;

// The mode a worker serves, `read` or `write` on its command line. It decides the outcomes the worker writes itself,
// Failed for a Read and Unknown for a Write where the two differ, and a Read worker never runs without safe mode (D12).
internal enum WorkerModeEnum
{
    Read,
    Write,
}
