using System.Runtime.Versioning;
using System.Text;

namespace Moltaro.OnecOutpost.ComWorker;

// The worker program: Moltaro.OnecOutpost.ComWorker.exe <base key> <read|write>. The base key only names the worker in
// its diagnostics, and any other command line ends it with WorkerExitCodes.BadInput. It points Console.Out at the
// standard error, so that the standard output carries the lines of Worker and nothing else, and calls Worker.Run with
// ComConnector and the raw standard streams. It has no test entry point: only the lab tests start it as a process.
[SupportedOSPlatform("windows")]
internal static class Program
{
    // The thread is a single-threaded apartment, as that of the Windows PowerShell that reached the COM connector in
    // the lab check L1: it creates the connector and makes every call to the objects of 1C.
    [STAThread]
    private static int Main(string[] args)
    {
        // The diagnostics are UTF-8 without a BOM, as the lines, and reach the standard error as they are written. The
        // streams live as long as the process, so nothing disposes of them.
        StreamWriter diagnostics = new(Console.OpenStandardError(), new UTF8Encoding(false)) { AutoFlush = true };

        // Whatever writes to the console writes to the standard error, so that the standard output carries the lines of
        // Worker and nothing else.
        Console.SetOut(diagnostics);
        Console.SetError(diagnostics);

        WorkerModeEnum? mode = args.Length == 2 && args[0].Length > 0 ? ModeOf(args[1]) : null;

        if (mode is null)
        {
            diagnostics.WriteLine("Usage: Moltaro.OnecOutpost.ComWorker <base key> <read|write>");

            return WorkerExitCodes.BadInput;
        }

        Worker worker = new(new ComConnector(), mode.Value);
        int exitCode = worker.Run(Console.OpenStandardInput(), Console.OpenStandardOutput(), diagnostics);

        if (exitCode != WorkerExitCodes.Ended)
        {
            diagnostics.WriteLine($"The COM worker of the base {args[0]}, mode {args[1]}, ends with exit code {exitCode}.");
        }

        return exitCode;
    }

    private static WorkerModeEnum? ModeOf(string mode)
    {
        return mode switch
        {
            "read" => WorkerModeEnum.Read,
            "write" => WorkerModeEnum.Write,
            _ => null,
        };
    }
}
