using System.Text;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Policy;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Console.Tests.Metadata;

// check's rule before a worker starts, which metadata holds too (the S10 brief, "metadata <BASE> <FILE>", step 4, and
// review 3, entry 2): the files; a base that the policy does not name; the passwords that the worker of the base needs,
// its read password and, for a base with a worker account, the worker account's, each named alone; the connector; the
// data folder, the program folder and the folder of the connector, which somebody else could have planted or changed;
// and the one logon of the worker account. A line of that rule that failed ends metadata with CheckFailed, naming the
// line as check names it, with its reason, and no worker starts and no probe runs; a base the policy does not name ends
// it with NotInPolicy before the connector is looked for. A password that the worker does not need, the write password
// or another base's, refuses nothing and is not named. Nothing is written.
public sealed class MetadataGateTests : IDisposable
{
    private readonly MetadataMachine _metadata = new();

    private TestMachine Machine => _metadata.Machine;

    public void Dispose()
    {
        _metadata.Dispose();
    }

    [Fact]
    public async Task A_policy_that_is_invalid_ends_metadata_with_CheckFailed_and_the_reason_of_the_agent()
    {
        const string Policy = """{ "bases": [] }""";
        Machine.Data.Root.Seed(AgentFolders.PolicyName, Policy);
        AgentPolicyException exception = Assert.Throws<AgentPolicyException>(() => AgentPolicyReader.ReadFile(Encoding.UTF8.GetBytes(Policy)));

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(run, $"Files {AgentFolders.PolicyName}", StartChecks.PolicyReason(exception));
        Assert.Empty(Machine.Workers.LogOns);
    }

    // pair refuses while agent.json exists, so the remedy says to remove it first.
    [Theory]
    [InlineData("absent", "secrets.dat is absent; to pair the agent anew, remove agent.json, if any, and run the console's pair")]
    [InlineData("not decrypted", "secrets.dat cannot be decrypted on this machine; to pair the agent anew, remove agent.json, if any, and run the console's pair")]
    public async Task A_secrets_dat_that_cannot_be_used_ends_metadata_with_CheckFailed_and_its_reason(string fault, string reason)
    {
        if (fault == "absent")
        {
            Machine.Data.Root.Unseed(AgentFolders.SecretsName);
        }
        else
        {
            Machine.Protection.Refuses = true;
        }

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(run, $"Files {AgentFolders.SecretsName}", reason);
        Assert.Empty(Machine.Workers.LogOns);
    }

    [Fact]
    public async Task A_base_that_the_policy_does_not_name_ends_metadata_with_NotInPolicy_before_the_connector_is_looked_for()
    {
        MetadataRun run = _metadata.Ended(
            await Machine.RunAsync("metadata", "ledger", _metadata.ReportOf("ledger")),
            "ledger");

        run.AssertEnded(ConsoleExitCodes.NotInPolicy, "NotInPolicy (14): the policy names no base ledger");
        Assert.DoesNotContain("find", Machine.Operations);
        AssertNoWorker();
    }

    // The read password of the base is the one named, alone, though its write password and its worker account's are
    // missing too.
    [Fact]
    public async Task A_missing_read_password_of_the_base_ends_metadata_with_CheckFailed_naming_that_password_alone()
    {
        _metadata.WriteSecrets(Secrets(
            new Dictionary<(string Base, OutpostModeEnum Mode), string> { [("warehouse", OutpostModeEnum.Read)] = MetadataMachine.WarehousePassword },
            new Dictionary<string, string>()));

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(
            run,
            $"Files {AgentFolders.SecretsName}",
            "secrets.dat holds no password for the read mode of the base accounting; write it with secret accounting read");
        Assert.DoesNotContain("the write mode of the base accounting", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("the worker account of the base accounting", run.Output, StringComparison.Ordinal);
        Assert.Empty(Machine.Workers.LogOns);
    }

    [Fact]
    public async Task A_missing_password_of_the_worker_account_of_the_base_ends_metadata_with_CheckFailed_naming_it()
    {
        _metadata.WriteSecrets(Secrets(
            new Dictionary<(string Base, OutpostModeEnum Mode), string>
            {
                [("accounting", OutpostModeEnum.Read)] = MetadataMachine.ReadPassword,
                [("accounting", OutpostModeEnum.Write)] = MetadataMachine.WritePassword,
                [("warehouse", OutpostModeEnum.Read)] = MetadataMachine.WarehousePassword,
            },
            new Dictionary<string, string>()));

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(
            run,
            $"Files {AgentFolders.SecretsName}",
            "secrets.dat holds no password for the worker account of the base accounting; write it with secret accounting worker");
        Assert.Empty(Machine.Workers.LogOns);
    }

    // metadata runs Reads alone, under the read account, so the write password is not needed, nor any of another base.
    [Fact]
    public async Task A_missing_write_password_or_one_of_another_base_neither_refuses_nor_is_named()
    {
        _metadata.WriteSecrets(Secrets(
            new Dictionary<(string Base, OutpostModeEnum Mode), string> { [("accounting", OutpostModeEnum.Read)] = MetadataMachine.ReadPassword },
            new Dictionary<string, string> { ["accounting"] = MetadataMachine.WorkerPassword }));

        MetadataRun run = await _metadata.RunAsync();

        run.AssertSucceeded(read: 6, found: 6, failed: 0);
        Assert.DoesNotContain("holds no password", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Without_a_registered_connector_metadata_ends_with_CheckFailed_and_the_line_of_the_connector()
    {
        Machine.Connectors.Uninstall(ConnectorBitnessEnum.X64);

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(run, "Connector", "none is registered or installed");
    }

    // A local user admitted to the data folder could have planted a policy whose base runs code of his own, with
    // secrets that he encrypted for the machine.
    [Fact]
    public async Task A_data_folder_that_admits_somebody_else_ends_metadata_with_CheckFailed_and_the_reason_of_the_start()
    {
        Machine.Access.Admit("", @"BUILTIN\Users");

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(run, $"AccessLists {TestMachine.DataFolder}", StartChecks.AccessReason("", Machine.Access.Others(""))!);
    }

    [Fact]
    public async Task A_data_folder_that_is_a_reparse_point_ends_metadata_with_CheckFailed()
    {
        Machine.Folders.Kinds[TestMachine.DataFolder] = FolderKindEnum.ReparsePoint;

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(
            run,
            $"AccessLists {TestMachine.DataFolder}",
            $"{TestMachine.DataFolder} is a reparse point; inspect it and correct it, never remove what it holds");
    }

    // Somebody who may change the worker could run code under the worker account, or under the console's administrator.
    [Fact]
    public async Task A_program_folder_that_somebody_else_may_change_ends_metadata_with_CheckFailed_and_the_reason_of_the_start()
    {
        Machine.Access.LetChange(MetadataMachine.Program, @"PC-1\intruder");

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(
            run,
            $"AccessLists {TestMachine.ProgramFolder}",
            StartChecks.WritersReason(MetadataMachine.Program, [@"PC-1\intruder"]));
    }

    [Fact]
    public async Task A_folder_of_the_connector_that_somebody_else_may_change_ends_metadata_with_CheckFailed_and_the_reason_of_the_start()
    {
        string connector = Path.Combine(MetadataMachine.ConnectorFolder, "comcntr.dll");
        Machine.Access.LetChange(connector, @"PC-1\intruder");

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(run, $"AccessLists {MetadataMachine.ConnectorFolder}", StartChecks.WritersReason(connector, [@"PC-1\intruder"]));
    }

    // Each failed logon counts toward a lockout, so the account is logged on once, never again.
    [Fact]
    public async Task A_worker_account_that_cannot_log_on_ends_metadata_with_CheckFailed_after_one_logon()
    {
        Machine.Workers.LogOnErrors[MetadataMachine.WorkerAccount] = 1326;

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(
            run,
            $"WorkerAccount accounting {MetadataMachine.WorkerAccount}",
            $"{MetadataMachine.WorkerAccount} cannot log on: {WorkerAccountReasons.CannotLogOn("accounting", 1326)}");
        Assert.Single(Machine.Workers.LogOns);
    }

    [Fact]
    public async Task A_worker_account_whose_token_is_beyond_an_ordinary_user_ends_metadata_with_CheckFailed_and_is_closed()
    {
        Machine.Workers.Administrators.Add(MetadataMachine.WorkerAccount);

        MetadataRun run = await _metadata.RunAsync();

        AssertRefused(
            run,
            $"WorkerAccount accounting {MetadataMachine.WorkerAccount}",
            $"{MetadataMachine.WorkerAccount} {WorkerAccountReasons.NotOrdinary}");
        Assert.Single(Machine.Workers.LogOns);
        Assert.All(Machine.Workers.Accounts, account => Assert.True(account.IsDisposed, $"{account.Account} was not disposed."));
    }

    // The secrets of the machine with these passwords.
    private AgentSecrets Secrets(
        Dictionary<(string Base, OutpostModeEnum Mode), string> passwords,
        Dictionary<string, string> workerPasswords)
    {
        return new AgentSecrets(_metadata.Secrets().PrivateKey, passwords, workerPasswords);
    }

    // metadata ended with CheckFailed, naming the line of check's rule that failed and its reason, and started no
    // worker, ran no probe and wrote nothing.
    private void AssertRefused(MetadataRun run, string line, string reason)
    {
        run.AssertEnded(
            ConsoleExitCodes.CheckFailed,
            $"CheckFailed (19): the line {line} of check failed, so metadata starts no worker: {reason}");
        AssertNoWorker();
    }

    private void AssertNoWorker()
    {
        Assert.Empty(Machine.Workers.Processes);
        Assert.DoesNotContain(Machine.Operations, operation => operation.StartsWith("probe ", StringComparison.Ordinal));
        Assert.DoesNotContain(Machine.Operations, operation => operation.StartsWith("start ", StringComparison.Ordinal));
        Assert.Empty(Machine.Routes);
    }
}
