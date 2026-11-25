using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// The report written whole or not at all: under a temporary name of its own beside the file, created new, flushed and
// closed, then moved
// to the file without overwriting, so that it never replaces a file, not even one made during the walk, which then ends
// it with Failure and leaves no temporary file. Ctrl+C between two Reads ends it with Cancelled before any file exists.
public sealed class MetadataReportFileTests : IDisposable
{
    private readonly MetadataMachine _metadata = new();

    private TestMachine Machine => _metadata.Machine;

    public void Dispose()
    {
        _metadata.Dispose();
    }

    // Somebody makes the file while the walk reads the common storage, its last Read.
    [Fact]
    public async Task A_file_made_during_the_walk_ends_metadata_with_Failure_stays_as_it_was_and_no_temporary_file_is_left()
    {
        string file = _metadata.ReportOf("accounting");
        _metadata.Accounting.Reading = read =>
        {
            if (read == "common")
            {
                File.WriteAllText(file, "made during the walk");
            }
        };

        MetadataRun run = await _metadata.RunAsync();

        Assert.True(run.ExitCode == ConsoleExitCodes.Failure, $"The run ended with {run.ExitCode}; its output:\n{run.Output}");
        Assert.Contains(run.Lines, line => line.StartsWith("Failure (1): the step \"report\" failed: ", StringComparison.Ordinal));
        Assert.Equal("made during the walk", File.ReadAllText(file));
        Assert.Equal([file], Directory.GetFileSystemEntries(_metadata.Reports.Path));
    }

    // Ctrl+C cancels the token of the run while the census is read; metadata checks it before the next Read.
    [Fact]
    public async Task Ctrl_C_between_two_Reads_ends_metadata_with_Cancelled_before_any_file_exists()
    {
        using CancellationTokenSource cancellation = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);
        _metadata.Accounting.Reading = read =>
        {
            if (read == "census")
            {
                cancellation.Cancel();
            }
        };

        CommandAppResult result = await TestTimes.EndOf(
            Machine.Start(cancellation.Token, "metadata", "accounting", _metadata.ReportOf("accounting")));
        MetadataRun run = _metadata.Ended(result);

        run.AssertEnded(ConsoleExitCodes.Cancelled, "Cancelled (5): the command was cancelled");
        Assert.Equal(["census"], _metadata.Accounting.Answers.Select(answer => answer.Read));
        Assert.All(Machine.Workers.Processes, process => Assert.True(process.IsDisposed, $"The worker of {process.Base} was not disposed."));
    }
}
