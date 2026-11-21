namespace Moltaro.OnecOutpost.ComWorker;

// The worker program: Moltaro.OnecOutpost.ComWorker.exe <base key> <read|write>. The base key only names the worker in
// its diagnostics, and any other command line ends it with WorkerExitCodes.BadInput. It points Console.Out at the
// standard error, so that the standard output carries the lines of Worker and nothing else, and calls Worker.Run with
// ComConnector and the raw standard streams. It has no test entry point: only the lab tests start it as a process.
// Not implemented yet.
internal static class Program
{
    private static int Main(string[] args)
    {
        throw new NotSupportedException();
    }
}
