namespace Moltaro.OnecOutpost.ComWorker;

// The exit codes of the worker program, which Worker.Run returns. The agent does not read them, since it keeps nothing
// of a worker that ended; the tests of the worker read them, on the fake connection and on the lab's 1C.
internal static class WorkerExitCodes
{
    // The input ended: before the first line, or once the command the worker ran had its outcome written.
    public const int Ended = 0;

    // The command line, or a line of the input, could not be read: not JSON, a member missing, unknown or of a wrong
    // type. Also a Read worker given "safeMode":false (D12). The reason goes to the standard error; no line is skipped,
    // and the worker writes nothing to the standard output for it.
    public const int BadInput = 1;

    // No session opened, after {"ready":false}; or the executor did not open, after the outcome Failed
    // base-unreachable.
    public const int NoSession = 2;

    // The call of the executor's Run threw, after the outcome connection-lost: Failed for a Read, Unknown for a Write.
    public const int SessionLost = 3;
}
