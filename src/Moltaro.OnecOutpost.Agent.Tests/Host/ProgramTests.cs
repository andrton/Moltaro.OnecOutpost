using System.Diagnostics;
using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Host;

namespace Moltaro.OnecOutpost.Agent.Tests.Host;

// The agent program on Windows (the S6 brief, "Program", "AgentService.Run" and "AgentExitCodes"), with
// Requires=Windows: started with service by anything but the service controller, the dispatcher fails with error 1063
// and the program exits with NotService before it touches its data folder, %ProgramData%\Moltaro.OnecOutpost, which
// the test only reads; with any other arguments it exits with Usage and never waits on the service controller. The
// program is the one built beside the tests. A real service is the manual acceptance of the brief.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class ProgramTests
{
    private static readonly string DataFolder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
        "Moltaro.OnecOutpost");

    [Fact]
    public async Task The_program_started_with_service_outside_the_service_controller_exits_with_NotService_and_changes_nothing_in_its_data_folder()
    {
        string before = Describe(DataFolder);

        int exitCode = await RunAsync("service");

        Assert.Equal(AgentExitCodes.NotService, exitCode);
        Assert.Equal(before, Describe(DataFolder));
    }

    [Theory]
    [InlineData]
    [InlineData("serve")]
    [InlineData("service", "now")]
    public async Task Any_arguments_but_service_exit_with_Usage(params string[] arguments)
    {
        Assert.Equal(AgentExitCodes.Usage, await RunAsync(arguments));
    }

    // Runs the program with the arguments to its end, which comes within the deadline of the tests, and gives its exit
    // code. Its output is read so that it never waits on a full pipe.
    private static async Task<int> RunAsync(params string[] arguments)
    {
        ProcessStartInfo start = new(Path.Combine(AppContext.BaseDirectory, "Moltaro.OnecOutpost.Agent.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
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

        try
        {
            Task<string> output = program.StandardOutput.ReadToEndAsync(deadline.Token);
            Task<string> error = program.StandardError.ReadToEndAsync(deadline.Token);
            await program.WaitForExitAsync(deadline.Token);
            await Task.WhenAll(output, error);
        }
        catch (OperationCanceledException)
        {
            program.Kill(entireProcessTree: true);
            throw;
        }

        return program.ExitCode;
    }

    // What the data folder holds, read only: whether it exists, and each entry with its length and the time it was last
    // written, or that it cannot be read.
    private static string Describe(string folder)
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
