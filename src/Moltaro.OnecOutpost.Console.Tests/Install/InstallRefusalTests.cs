using System.ComponentModel;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Service;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Install;

// How install ends before its last step (the S10 brief, "install"), each failure at its step, with its code and its
// line, and nothing after it. A step that fails for a reason of Windows or of the disk ends with Failure and
// the step "{step}" failed: {message}, with (Windows error {code}) for an error of Windows; the others end with the
// codes of their own: Usage for a console run from inside the program folder, StopTimedOut, ProgramFolderWriters and
// DataFolderAdmitsOthers. install repairs neither folder that admits somebody else.
public sealed class InstallRefusalTests
{
    private static readonly string Stop = $"stop {TestMachine.ServiceName} within 00:01:00";

    private static readonly string ApplyProgramFolder = $"apply program folder {TestMachine.ProgramFolder} from {TestMachine.Archive}";

    private static readonly string Writers = $"writers {TestMachine.ProgramFolder}";

    private static readonly string Install =
        $"install {TestMachine.ServiceName} as Moltaro OnecOutpost agent running "
        + $"\"{Path.Combine(TestMachine.ProgramFolder, "Moltaro.OnecOutpost.Agent.exe")}\" service";

    private static readonly string Grant = $"grant {TestMachine.AgentSid} SeAssignPrimaryTokenPrivilege, SeIncreaseQuotaPrivilege";

    private readonly TestMachine _machine = new();

    // The archive unpacked into a folder of the program folder, whose content install would empty before it copied.
    [Fact]
    public async Task Install_run_from_inside_the_program_folder_ends_with_Usage_before_anything_changes()
    {
        _machine.Source = Path.Combine(TestMachine.ProgramFolder, "unpacked") + Path.DirectorySeparatorChar;

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Contains(
            $"Usage (2): unpack the archive outside {TestMachine.ProgramFolder} and run install from there",
            TestMachine.LinesOf(result));
        Assert.Empty(_machine.Operations);
    }

    // The archive unpacked straight into %ProgramFiles%, or into the root of the volume: install would copy all that
    // folder holds into the program folder, the program folder included.
    [Theory]
    [InlineData("Program Files")]
    [InlineData("")]
    public async Task Install_run_from_a_folder_that_holds_the_program_folder_ends_with_Usage_before_anything_changes(string folder)
    {
        _machine.Source = Path.Combine(TestMachine.Root, folder);

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Usage, result.ExitCode);
        Assert.Contains(
            $"Usage (2): unpack the archive outside {TestMachine.ProgramFolder} and run install from there",
            TestMachine.LinesOf(result));
        Assert.Empty(_machine.Operations);
    }

    [Fact]
    public async Task A_service_that_does_not_stop_within_the_stop_timeout_ends_install_with_StopTimedOut()
    {
        _machine.HasService();
        _machine.Services.StopResult = ServiceStopEnum.TimedOut;

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.StopTimedOut, result.ExitCode);
        Assert.Contains(
            $"StopTimedOut (7): the service {TestMachine.ServiceName} did not stop within 00:01:00; run install again once it "
            + "has, or with a longer --stop-timeout",
            TestMachine.LinesOf(result));
        Assert.Equal([Stop], _machine.Operations);
    }

    [Fact]
    public async Task A_stop_that_Windows_refuses_ends_install_with_Failure_naming_the_step_and_the_error()
    {
        _machine.Services.StopFailure = new Win32Exception(5, "the fake service controller refused");

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): the step \"stop\" failed: the fake service controller refused (Windows error 5)", result.Output);
        Assert.Equal([Stop], _machine.Operations);
    }

    [Fact]
    public async Task A_program_folder_that_cannot_be_written_ends_install_with_Failure_naming_the_step()
    {
        _machine.Folders.ProgramFolderFailure = new IOException("the fake disk is full");

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): the step \"program folder\" failed: the fake disk is full", result.Output);
        Assert.Equal([Stop, ApplyProgramFolder], _machine.Operations);
    }

    // The agent's own rule of S9c, judged before any service exists that would run the code of that folder.
    [Fact]
    public async Task A_program_folder_that_someone_else_may_change_ends_install_with_ProgramFolderWriters_and_a_table_of_the_entries()
    {
        string root = Path.GetPathRoot(TestMachine.ProgramFolder)!;
        string worker = Path.Combine(TestMachine.ProgramFolder, "x64", "Moltaro.OnecOutpost.ComWorker.exe");
        _machine.Access.LetChange(root, @"NT AUTHORITY\Authenticated Users");
        _machine.Access.LetChange(worker, @"OFFICE\clerk", @"BUILTIN\Users");

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.ProgramFolderWriters, result.ExitCode);
        Assert.Contains("ProgramFolderWriters (8): ", result.Output);
        Assert.Contains(root, result.Output);
        Assert.Contains(@"NT AUTHORITY\Authenticated Users", result.Output);
        Assert.Contains(worker, result.Output);
        Assert.Contains(@"OFFICE\clerk", result.Output);
        Assert.Contains(@"BUILTIN\Users", result.Output);
        Assert.Equal([Stop, ApplyProgramFolder, Writers], _machine.Operations);
    }

    [Fact]
    public async Task Access_lists_of_the_program_folder_that_cannot_be_read_end_install_with_Failure_naming_the_step()
    {
        _machine.Access.MakeProgramsUnreadable();

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): the step \"program folder\" failed: ", result.Output);
        Assert.Equal([Stop, ApplyProgramFolder, Writers], _machine.Operations);
    }

    // 1072: the service is marked for deletion, and a handle that the Services console or Event Viewer holds keeps it.
    [Fact]
    public async Task A_service_marked_for_deletion_ends_install_with_Failure_and_the_remedy()
    {
        _machine.Services.InstallFailure = new Win32Exception(1072, "The specified service has been marked for deletion.");

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): ", result.Output);
        Assert.Contains("close the Services console and Event Viewer, and run install again", result.Output);
        Assert.Equal([Stop, ApplyProgramFolder, Writers, Install], _machine.Operations);
    }

    [Fact]
    public async Task Rights_that_LSA_refuses_end_install_with_Failure_naming_the_step_and_the_error()
    {
        _machine.Rights.GrantFailure = new Win32Exception(1332, "the fake LSA refused");

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): the step \"rights\" failed: the fake LSA refused (Windows error 1332)", result.Output);
        Assert.Equal([Stop, ApplyProgramFolder, Writers, Install, Grant], _machine.Operations);
    }

    // A junction in the place of the data folder, or of a folder in it, would lead what the agent writes elsewhere.
    [Theory]
    [InlineData("")]
    [InlineData("audit")]
    [InlineData("outcomes")]
    public async Task A_data_folder_or_a_folder_of_it_that_is_a_reparse_point_ends_install_with_DataFolderAdmitsOthers(string folder)
    {
        _machine.Installed();
        _machine.Folders.Kinds[folder.Length == 0 ? TestMachine.DataFolder : Path.Combine(TestMachine.DataFolder, folder)] =
            FolderKindEnum.ReparsePoint;

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.DataFolderAdmitsOthers, result.ExitCode);
        Assert.Contains("DataFolderAdmitsOthers (9): ", result.Output);
        Assert.Contains("is a reparse point; install does not repair it: inspect it, remove it and run install again", result.Output);
        AssertNamed(folder, result);
        Assert.DoesNotContain(_machine.Operations, line => line.StartsWith("apply data folder", StringComparison.Ordinal));
    }

    // %ProgramData% lets every local user create a folder, so one made before install may admit its maker.
    [Theory]
    [InlineData("")]
    [InlineData("audit")]
    [InlineData("outcomes")]
    public async Task A_data_folder_or_a_folder_of_it_that_admits_anyone_else_ends_install_with_DataFolderAdmitsOthers_naming_them(string folder)
    {
        _machine.Installed();
        _machine.Access.Admit(folder, @"OFFICE\clerk");

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.DataFolderAdmitsOthers, result.ExitCode);
        Assert.Contains("DataFolderAdmitsOthers (9): ", result.Output);
        Assert.Contains(@"admits OFFICE\clerk; install does not repair it: inspect it, remove it and run install again", result.Output);
        AssertNamed(folder, result);
        Assert.Equal($"others \"{folder}\"", _machine.Operations[^1]);
        Assert.DoesNotContain(_machine.Operations, line => line.StartsWith("apply data folder", StringComparison.Ordinal));
    }

    // Such as 183 of CreateDirectoryW, when a folder appeared since its kind was read.
    [Fact]
    public async Task A_data_folder_that_cannot_be_written_ends_install_with_Failure_naming_the_step()
    {
        _machine.Folders.DataFolderFailure = new IOException("the fake folder exists already");

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Failure, result.ExitCode);
        Assert.Contains("Failure (1): the step \"data folder\" failed: the fake folder exists already", result.Output);
        Assert.Equal($"apply data folder {TestMachine.DataFolder} for {TestMachine.AgentSid}, creating it", _machine.Operations[^1]);
    }

    // A folder of the data folder is named as {folder} of {data folder}.
    private static void AssertNamed(string folder, CommandAppResult result)
    {
        Assert.Contains(folder.Length == 0 ? TestMachine.DataFolder : $"{folder} of {TestMachine.DataFolder}", result.Output);
    }
}
