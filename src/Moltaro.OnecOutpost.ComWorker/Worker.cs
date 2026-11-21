namespace Moltaro.OnecOutpost.ComWorker;

// The whole behavior of the worker program over its standard streams. It reads the connection line, opens the session
// and answers whether it is ready; then, for each command line, it opens a fresh executor, calls Run, writes the line
// that Run returned and disposes of the executor. The worker depends on nothing outside .NET (principle 12), so it
// reads and writes its lines with System.Text.Json and shares no type with the protocol assembly. The loop is
// synchronous: the worker is one thread that owns one COM session and has nothing else to do while a script runs.
internal sealed class Worker
{
    // Opens the one session of the worker; ComConnector in the program, a fake in the tests.
    private readonly IOneCConnector _connector;

    // The mode of the command line. A Read worker refuses to run without safe mode, and the outcomes the worker writes
    // itself are Failed for a Read and Unknown for a Write where the two differ.
    private readonly WorkerModeEnum _mode;

    public Worker(IOneCConnector connector, WorkerModeEnum mode)
    {
        _connector = connector;
        _mode = mode;
    }

    // Runs the worker until its input ends or a fault ends it, and returns one of WorkerExitCodes. The input and the
    // output carry one JSON object per line in UTF-8, ended with LF, without a BOM; the output carries these lines and
    // nothing else, and an outcome line goes out as the executor returned it, with its LF, in one write. The worker
    // reads a line to a depth of 72 levels, the protocol's limit of a message, so that parameters of 64 levels pass.
    // The diagnostics are for a person: the first input line is never written there, and the password is replaced
    // with <password> in every text written there. Not implemented yet.
    public int Run(Stream input, Stream output, TextWriter diagnostics)
    {
        throw new NotSupportedException();
    }
}
