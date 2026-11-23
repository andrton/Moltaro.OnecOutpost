using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Spectre.Console.Cli.Testing;

namespace Moltaro.OnecOutpost.Console.Tests.Install;

// install [--stop-timeout <TIME>] (the S10 brief, "install", and S9c's drafts), each step idempotent and in this order:
// the stop of the service; the program folder, copied from where the console runs unless it runs from the program
// folder itself, then judged by the agent's rule before any service exists; the service under NT SERVICE\{name}; the
// two privileges of its account; the data folder, created when absent, else judged and given its access lists again.
// It never starts the service, and an update keeps what was written before.
public sealed class InstallTests
{
    private static readonly string BinaryPath = $"\"{Path.Combine(TestMachine.ProgramFolder, "Moltaro.OnecOutpost.Agent.exe")}\" service";

    private static readonly string Account = $@"NT SERVICE\{TestMachine.ServiceName}";

    private readonly TestMachine _machine = new();

    [Fact]
    public async Task Install_on_a_fresh_machine_copies_the_program_registers_the_service_grants_its_rights_and_creates_the_data_folder()
    {
        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal(
            [
                $"stop {TestMachine.ServiceName} within 00:01:00",
                $"apply program folder {TestMachine.ProgramFolder} from {TestMachine.Archive}",
                $"writers {TestMachine.ProgramFolder}",
                $"install {TestMachine.ServiceName} as Moltaro OnecOutpost agent running {BinaryPath}",
                $"grant {TestMachine.AgentSid} SeAssignPrimaryTokenPrivilege, SeIncreaseQuotaPrivilege",
                $"apply data folder {TestMachine.DataFolder} for {TestMachine.AgentSid}, creating it",
            ],
            _machine.Operations);
        Assert.Equal([TestMachine.AgentSid], _machine.AccessListAccounts.Distinct());

        IReadOnlyList<string> lines = TestMachine.LinesOf(result);
        Assert.Contains($"Copied the program to {TestMachine.ProgramFolder} and set its access lists.", lines);
        Assert.Contains($"Registered the service {TestMachine.ServiceName} under {Account}.", lines);
        Assert.Contains(
            $"Granted {Account} \"Replace a process level token\" and \"Adjust memory quotas for a process\".",
            lines);
        Assert.Contains(lines, line =>
            line.StartsWith("Created ", StringComparison.Ordinal) && line.Contains(TestMachine.DataFolder, StringComparison.Ordinal));
        Assert.Equal(
            "Installed; the service is not started. Start it once policy.json, the pairing and the passwords are written; an "
            + "update keeps those written before.",
            lines[^1]);
    }

    // An update: the service exists and is stopped first, the data folder exists with what install, pair and secret
    // wrote, and the files in it are not touched.
    [Fact]
    public async Task Install_over_an_installed_agent_reconfigures_the_service_and_sets_the_access_lists_of_the_data_folder_again()
    {
        _machine.HasService();
        _machine.Installed();

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal(
            [
                $"stop {TestMachine.ServiceName} within 00:01:00",
                $"apply program folder {TestMachine.ProgramFolder} from {TestMachine.Archive}",
                $"writers {TestMachine.ProgramFolder}",
                $"install {TestMachine.ServiceName} as Moltaro OnecOutpost agent running {BinaryPath}",
                $"grant {TestMachine.AgentSid} SeAssignPrimaryTokenPrivilege, SeIncreaseQuotaPrivilege",
                "others \"\"",
                "others \"audit\"",
                "others \"outcomes\"",
                $"apply data folder {TestMachine.DataFolder} for {TestMachine.AgentSid}",
            ],
            _machine.Operations);

        IReadOnlyList<string> lines = TestMachine.LinesOf(result);
        Assert.Contains($"Reconfigured the service {TestMachine.ServiceName} under {Account}.", lines);
        Assert.Contains($"Set the access lists of {TestMachine.DataFolder}.", lines);
    }

    // As after the program folder was unpacked by hand, or install is run again from the copy it made: the console runs
    // from the program folder, named as a short name and with an ending separator as AppContext.BaseDirectory has it,
    // which the final path makes the same folder. Nothing is copied; the lists alone are set.
    [Fact]
    public async Task Install_run_from_the_program_folder_sets_its_access_lists_without_copying_it()
    {
        string shortName = Path.Combine(TestMachine.Root, "PROGRA~1", "MOLTAR~1");
        _machine.Folders.FinalPaths[shortName] = TestMachine.ProgramFolder;
        _machine.Source = shortName + Path.DirectorySeparatorChar;

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal($"apply program folder {TestMachine.ProgramFolder}", _machine.Operations[1]);
        Assert.Contains(TestMachine.LinesOf(result), line =>
            line.StartsWith("Set ", StringComparison.Ordinal) && line.Contains(TestMachine.ProgramFolder, StringComparison.Ordinal));
    }

    // A folder whose name begins with that of the program folder is beside it, not inside it.
    [Fact]
    public async Task Install_run_from_a_folder_beside_the_program_folder_copies_from_it()
    {
        _machine.Source = TestMachine.ProgramFolder + "-0.1.0";

        CommandAppResult result = await _machine.RunAsync("install");

        Assert.Equal(ConsoleExitCodes.Success, result.ExitCode);
        Assert.Equal($"apply program folder {TestMachine.ProgramFolder} from {_machine.Source}", _machine.Operations[1]);
    }
}
