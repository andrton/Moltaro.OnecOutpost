using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Check;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// The line AccessLists of check (the S10b brief, "check", and Q14): the data folder and every entry that the agent's
// start judges, StartChecks.DataPaths of the files listed, each named with StartChecks.AccessReason when it admits
// somebody else or somebody else owns it, a file gone before its list counting as gone; the program folder and the folder
// of each registered connector, each entry through which somebody else may change them named with
// StartChecks.WritersReason. check never starts code that somebody else may have changed: a data folder that admits
// somebody else, who could have planted a policy.json or a secrets.dat there (the contract amended after the first code
// review), a program folder or a folder of the connector with which the workers start that somebody else may change
// leaves the probe and the connections not checked, and no worker starts. The folder of a connector of the other
// bitness blocks nothing.
public sealed class AccessListsLineTests : IDisposable
{
    private const string AuditFile = "2026-11-21.jsonl";

    private const string Outcome = "9c1e2f3a-4b5c-4d6e-8f70-a1b2c3d4e5f6.json";

    private readonly CheckedMachine _checked = new();

    public AccessListsLineTests()
    {
        // An audit file, a file of the audit folder that is not one of the log, and an outcome not yet sent.
        Machine.Data.Audit.Seed(AuditFile, "{}\n");
        Machine.Data.Audit.Seed("notes.txt", "the administrator's notes");
        Machine.Data.Outcomes.Seed(Outcome, "{}");
    }

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    // The access lists for the agent's account, of each entry that the start judges: the folder, its two folders, the
    // three files, the files of the log and the outbox, and nothing else.
    [Fact]
    public async Task Check_judges_the_data_folder_its_two_folders_and_each_file_that_the_start_judges_for_the_agent_account()
    {
        CheckRun run = await _checked.RunAsync();

        Assert.Equal(
            ["", "audit", "outcomes", "policy.json", "agent.json", "secrets.dat", $@"audit\{AuditFile}", $@"outcomes\{Outcome}"],
            Machine.Access.Asked);
        Assert.Equal([TestMachine.AgentSid], Machine.AccessListAccounts.Distinct());
        Assert.Equal("admits nobody else, nor do its 5 files", run.Passed(CheckLineEnum.AccessLists, TestMachine.DataFolder));
    }

    // The service may cut, rotate or send a file while check lists the folder: one gone before its list is not judged.
    [Fact]
    public async Task A_file_gone_between_the_listing_of_its_folder_and_its_list_counts_as_gone()
    {
        Machine.Access.Remove($@"audit\{AuditFile}");

        CheckRun run = await _checked.RunAsync();

        Assert.Equal("admits nobody else, nor do its 4 files", run.Passed(CheckLineEnum.AccessLists, TestMachine.DataFolder));
    }

    // Each entry named with the reason of the start, Q14's owner included, with its remedy; whoever an entry admits could
    // have planted the policy or the secrets with which a worker starts, so none starts.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Each_entry_of_the_data_folder_that_admits_somebody_else_or_that_somebody_else_owns_is_named_with_its_remedy_and_no_worker_starts()
    {
        Machine.Access.Admit($@"audit\{AuditFile}", @"PC-1\intruder", @"BUILTIN\Users");
        Machine.Access.Own(AgentFolders.SecretsName, @"PC-1\clerk");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        string reasons = Reasons(run, TestMachine.DataFolder);
        Assert.Contains(
            StartChecks.AccessReason($@"audit\{AuditFile}", Machine.Access.Others($@"audit\{AuditFile}"))!,
            reasons,
            StringComparison.Ordinal);
        Assert.Contains(
            StartChecks.AccessReason(AgentFolders.SecretsName, Machine.Access.Others(AgentFolders.SecretsName))!,
            reasons,
            StringComparison.Ordinal);
        Assert.Contains("icacls", reasons, StringComparison.Ordinal);
        AssertNoWorkerStarted(run, TestMachine.DataFolder);
    }

    // A local user may create the data folder before install, or be admitted to it, and plant there a policy whose base
    // runs code of his own, with secrets that he encrypted for the machine.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_data_folder_that_admits_somebody_else_is_named_and_no_worker_starts()
    {
        Machine.Access.Admit("", @"BUILTIN\Users");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Contains(
            StartChecks.AccessReason("", Machine.Access.Others(""))!,
            Reasons(run, TestMachine.DataFolder),
            StringComparison.Ordinal);
        AssertNoWorkerStarted(run, TestMachine.DataFolder);
    }

    [Fact]
    public async Task A_data_folder_that_is_a_reparse_point_fails_the_line_and_says_never_to_remove_what_it_holds()
    {
        Machine.Folders.Kinds[TestMachine.DataFolder] = FolderKindEnum.ReparsePoint;

        CheckRun run = await _checked.RunConfirmingAnyAsync();

        run.AssertCheckFailed();
        Assert.Contains(
            $"{TestMachine.DataFolder} is a reparse point; inspect it and correct it, never remove what it holds",
            Reasons(run, TestMachine.DataFolder),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Check_judges_who_may_change_the_program_folder_and_the_folder_of_the_registered_connector()
    {
        await _checked.RunAsync();

        Assert.Equal([TestMachine.ProgramFolder, CheckedMachine.ConnectorFolder], Machine.Access.ProgramsAsked);
    }

    // Somebody who may change the worker could run code under a worker account, or under the console's administrator.
    [Fact]
    public async Task A_program_folder_that_somebody_else_may_change_is_named_and_no_worker_starts()
    {
        string worker = CheckedMachine.Program;
        Machine.Access.LetChange(worker, @"PC-1\intruder", @"BUILTIN\Users");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Contains(
            StartChecks.WritersReason(worker, [@"PC-1\intruder", @"BUILTIN\Users"]),
            Reasons(run, TestMachine.ProgramFolder),
            StringComparison.Ordinal);
        run.Passed(CheckLineEnum.AccessLists, CheckedMachine.ConnectorFolder);
        AssertNoWorkerStarted(run, TestMachine.ProgramFolder);
    }

    [Fact]
    public async Task A_folder_of_the_connector_of_the_workers_that_somebody_else_may_change_is_named_and_no_worker_starts()
    {
        string connector = Path.Combine(CheckedMachine.ConnectorFolder, "comcntr.dll");
        Machine.Access.LetChange(connector, @"PC-1\intruder");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Contains(
            StartChecks.WritersReason(connector, [@"PC-1\intruder"]),
            Reasons(run, CheckedMachine.ConnectorFolder),
            StringComparison.Ordinal);
        run.Passed(CheckLineEnum.AccessLists, TestMachine.ProgramFolder);
        AssertNoWorkerStarted(run, CheckedMachine.ConnectorFolder);
    }

    // The workers start with the connector of 64 bits, so that of 32 bits is judged and named, and blocks none of them.
    [Fact]
    public async Task A_folder_of_a_connector_of_the_other_bitness_that_somebody_else_may_change_is_named_and_blocks_no_worker()
    {
        ConnectorFile other = Machine.Connectors.Registered(ConnectorBitnessEnum.X86, CheckedMachine.ConnectorVersion);
        string folder = Path.GetDirectoryName(other.Path)!;
        Machine.Access.LetChange(other.Path, @"PC-1\intruder");

        CheckRun run = await _checked.RunAsync();

        run.AssertCheckFailed();
        Assert.Contains(StartChecks.WritersReason(other.Path, [@"PC-1\intruder"]), Reasons(run, folder), StringComparison.Ordinal);
        run.Passed(CheckLineEnum.AccessLists, CheckedMachine.ConnectorFolder);
        run.Passed(CheckLineEnum.WorkerStart, "accounting");
        run.Passed(CheckLineEnum.Connection, "accounting", "outpost-read");
        run.Passed(CheckLineEnum.Connection, "warehouse", "outpost-read");
    }

    // The reasons of every failed row of the folder, one reason or several.
    private static string Reasons(CheckRun run, string folder)
    {
        List<string> reasons = [];

        foreach (CheckRow row in run.RowsOf(CheckLineEnum.AccessLists).Where(row => row.Check.Contains(folder, StringComparison.Ordinal)))
        {
            if (!row.Passed)
            {
                reasons.Add(row.Reason);
            }
        }

        Assert.True(reasons.Count > 0, $"No row of {folder} failed:\n{string.Join("\n", run.Rows)}");

        return string.Join("\n", reasons);
    }

    // The probe and every connection are not checked, naming the folder, and the worker account still logs on.
    private void AssertNoWorkerStarted(CheckRun run, string folder)
    {
        run.AssertNotChecked(CheckLineEnum.WorkerStart, folder, "accounting");
        run.AssertNotChecked(CheckLineEnum.Connection, folder, "accounting", "outpost-read");
        run.AssertNotChecked(CheckLineEnum.Connection, folder, "accounting", "outpost-write");
        run.AssertNotChecked(CheckLineEnum.Connection, folder, "warehouse", "outpost-read");
        run.Passed(CheckLineEnum.WorkerAccount, "accounting");
        Assert.Empty(Machine.Workers.Processes);
        Assert.DoesNotContain(Machine.Operations, line => line.StartsWith("probe ", StringComparison.Ordinal));
    }
}
