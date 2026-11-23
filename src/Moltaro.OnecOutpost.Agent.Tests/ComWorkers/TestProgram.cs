using System.Diagnostics;
using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The test program that stands in for the COM worker (the S9b brief), which the reference to its project builds and
// copies beside the tests: its file, a copy of it with the files of its build into a folder of a test, and a start as
// ComWorkerLauncher starts a worker, for the tests of ComWorkerProcess that hold the process themselves.
[SupportedOSPlatform("windows")]
internal static class TestProgram
{
    private const string Name = "Moltaro.OnecOutpost.TestWorker";

    public static string Path => System.IO.Path.Combine(AppContext.BaseDirectory, Name + ".exe");

    // Copies the program and the files of its build into the folder, which it creates, and gives the path of the copy.
    public static string CopyInto(string folder)
    {
        Directory.CreateDirectory(folder);

        foreach (string extension in new[] { ".exe", ".dll", ".runtimeconfig.json", ".deps.json" })
        {
            File.Copy(
                System.IO.Path.Combine(AppContext.BaseDirectory, Name + extension),
                System.IO.Path.Combine(folder, Name + extension));
        }

        return System.IO.Path.Combine(folder, Name + ".exe");
    }

    // Starts the program as the launcher starts a worker, with these arguments in place of the base key and the mode:
    // its standard input and output redirected, no shell and no window, then in the job.
    public static Process Start(WorkerJob job, params string[] arguments)
    {
        ProcessStartInfo start = new(Path)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        Process process = Process.Start(start) ?? throw new InvalidOperationException("The test program did not start.");
        job.Add(process);

        return process;
    }
}
