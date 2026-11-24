using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// The arguments of metadata and the step of its file (the S10 brief, "metadata <BASE> <FILE>", steps 1 and 2): a BASE
// that breaks the rule of a base key, with secret's sentence, and an empty FILE end it with Usage before any step;
// then, after the elevation, the file made full: one that exists, a file or a folder, ends it with Usage, since
// metadata never replaces a file, and so do a file whose folder does not exist and one whose folder, by its final path
// judged now, is or lies in the data folder, which metadata never writes. Each ends before anything is asked of the
// machine, so no worker starts and nothing is written.
public sealed class MetadataFileTests : IDisposable
{
    private readonly MetadataMachine _metadata = new();

    private TestMachine Machine => _metadata.Machine;

    public void Dispose()
    {
        _metadata.Dispose();
    }

    [Theory]
    [InlineData("Bad!")]
    [InlineData("Accounting")]
    [InlineData("")]
    public async Task A_base_key_that_breaks_the_rule_ends_metadata_with_Usage_and_the_rule_before_any_step(string baseKey)
    {
        CommandAppResult result = await Machine.RunAsync("metadata", baseKey, _metadata.ReportOf("accounting"));

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Contains($"Usage (2): {SecretCommand.Settings.BaseRule}", TestMachine.LinesOf(result));
        AssertNothingStarted();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_file_ends_metadata_with_Usage_before_any_step(string file)
    {
        CommandAppResult result = await Machine.RunAsync("metadata", "accounting", file);

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Contains("Usage (2): <FILE>: expected the path of a new file", TestMachine.LinesOf(result));
        AssertNothingStarted();
    }

    [Fact]
    public async Task A_file_that_exists_ends_metadata_with_Usage_and_stays_as_it_was()
    {
        string file = _metadata.ReportOf("accounting");
        File.WriteAllText(file, "an earlier report");

        MetadataRun run = await _metadata.RunAsync();

        Assert.Equal(ConsoleExitCodes.Usage, run.ExitCode);
        Assert.Contains($"Usage (2): {file} exists; metadata never replaces a file: name a new one", run.Lines);
        Assert.Equal("an earlier report", File.ReadAllText(file));
        Assert.Equal([file], Directory.GetFileSystemEntries(_metadata.Reports.Path));
        AssertNothingStarted();
    }

    [Fact]
    public async Task A_folder_at_the_path_of_the_file_ends_metadata_with_Usage()
    {
        string file = _metadata.ReportOf("accounting");
        Directory.CreateDirectory(file);

        MetadataRun run = await _metadata.RunAsync();

        Assert.Equal(ConsoleExitCodes.Usage, run.ExitCode);
        Assert.Contains($"Usage (2): {file} exists; metadata never replaces a file: name a new one", run.Lines);
        Assert.Empty(Directory.GetFileSystemEntries(file));
        AssertNothingStarted();
    }

    [Fact]
    public async Task A_file_whose_folder_does_not_exist_ends_metadata_with_Usage()
    {
        string file = Path.Combine(_metadata.Reports.Path, "missing", "accounting.json");

        CommandAppResult result = await Machine.RunAsync("metadata", "accounting", file);

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Contains($"Usage (2): the folder of {file} does not exist", TestMachine.LinesOf(result));
        Assert.Empty(Directory.GetFileSystemEntries(_metadata.Reports.Path));
        AssertNothingStarted();
    }

    // The folder of the file is judged by its final path, as a junction or a short name leads into the data folder: the
    // data folder itself, or a folder inside it such as audit\.
    [Theory]
    [InlineData("")]
    [InlineData("audit")]
    public async Task A_file_whose_folder_by_its_final_path_is_or_lies_in_the_data_folder_ends_metadata_with_Usage(string inside)
    {
        Machine.Folders.FinalPaths[_metadata.Reports.Path] = Path.Combine(TestMachine.DataFolder, inside).TrimEnd(Path.DirectorySeparatorChar);
        string file = _metadata.ReportOf("accounting");

        MetadataRun run = await _metadata.RunAsync();

        Assert.Equal(ConsoleExitCodes.Usage, run.ExitCode);
        Assert.Contains(
            $"Usage (2): {file} lies in the data folder {TestMachine.DataFolder}, which metadata never writes",
            run.Lines);
        run.AssertNoFile();
        AssertNothingStarted();
    }

    // A folder beside the data folder, whose name begins with the data folder's, lies outside it.
    [Fact]
    public async Task A_file_whose_folder_lies_beside_the_data_folder_under_a_longer_name_is_written()
    {
        Machine.Folders.FinalPaths[_metadata.Reports.Path] = TestMachine.DataFolder + "-reports";

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
    }

    // Nothing was asked of the machine: no operation, no worker, no logon, no server.
    private void AssertNothingStarted()
    {
        Assert.Empty(Machine.Operations);
        Assert.Empty(Machine.Workers.Processes);
        Assert.Empty(Machine.Workers.LogOns);
        Assert.Empty(Machine.Routes);
    }
}
