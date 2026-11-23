using System.Diagnostics;
using Xunit;

namespace Moltaro.OnecOutpost.TestSupport.Programs;

// A program of the repository as the reference to its project builds it and copies it beside the tests, such as the
// agent or the console, run as a child process with its standard streams redirected, as a script or a service manager
// runs it: no shell and no window, its input closed at once, and its output and its error read to their ends, so that
// it never waits on a full pipe. And what a folder holds, read only, for a test that states that a run of a program
// changed nothing there.
internal static class BuiltProgram
{
    // Runs the program, by its file name beside the tests, with the arguments to its end, which comes within the deadline
    // of the tests; a run that does not end then is killed and fails the test.
    public static async Task<ProgramEnd> RunAsync(string fileName, params string[] arguments)
    {
        ProcessStartInfo start = new(Path.Combine(AppContext.BaseDirectory, fileName))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };

        foreach (string argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        using Process program = Process.Start(start)!;
        using CancellationTokenSource deadline = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        deadline.CancelAfter(TestTimes.Deadline);

        program.StandardInput.Close();

        try
        {
            Task<string> output = program.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> error = program.StandardError.ReadToEndAsync(deadline.Token);
            await program.WaitForExitAsync(deadline.Token);

            return new ProgramEnd(program.ExitCode, await output, await error);
        }
        catch (OperationCanceledException)
        {
            program.Kill(entireProcessTree: true);
            throw;
        }
    }

    // What the folder holds, read only: whether it exists, and each entry with its length and the time it was last
    // written, or that it cannot be read.
    public static string Describe(string folder)
    {
        if (!Directory.Exists(folder))
        {
            return "absent";
        }

        try
        {
            IEnumerable<string> entries = Directory
                .EnumerateFileSystemEntries(folder, "*", SearchOption.AllDirectories)
                .Order(StringComparer.Ordinal)
                .Select(path => File.Exists(path)
                    ? $"{path} {new FileInfo(path).Length} {File.GetLastWriteTimeUtc(path):O}"
                    : $"{path} {Directory.GetLastWriteTimeUtc(path):O}");

            return $"{Directory.GetLastWriteTimeUtc(folder):O}\n{string.Join("\n", entries)}";
        }
        catch (UnauthorizedAccessException)
        {
            return $"{Directory.GetLastWriteTimeUtc(folder):O} unreadable";
        }
    }
}
