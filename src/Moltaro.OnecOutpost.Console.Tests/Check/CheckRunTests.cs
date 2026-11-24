using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Tests.FakeMachine;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Check;

namespace Moltaro.OnecOutpost.Console.Tests.Check;

// check on a machine where everything holds (the S10b brief, "check", and Acceptance): every line passes with its
// reason, and the table shows them in the order of the stage contract, CheckLineEnum's, then in the policy's order, and
// check ends with Success. Its steps come in their order: Files, Connector, AccessLists, AgentRights; per base
// WorkerAccount, WorkerStart, Connection read then write, Subscriptions and ReadRights; Server and ClockSkew. It only
// reads: it changes no file of the data folder and nothing of the machine, never polls, logs each worker account on once,
// and starts every worker in read mode and in safe mode, with the password of secrets.dat, under the base's worker
// account, or under the console's own token for a base without one.
public sealed class CheckRunTests : IDisposable
{
    private readonly CheckedMachine _checked = new();

    private TestMachine Machine => _checked.Machine;

    public void Dispose()
    {
        _checked.Dispose();
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task On_a_machine_where_everything_holds_every_line_passes_in_the_order_of_the_stage_contract_and_check_ends_with_Success()
    {
        CheckRun run = await _checked.RunAsync();

        run.AssertSucceeded();
        (CheckLineEnum Kind, string[] Subject)[] expected =
        [
            (CheckLineEnum.Connector, []),
            (CheckLineEnum.Connection, ["accounting", "outpost-read"]),
            (CheckLineEnum.Connection, ["accounting", "outpost-write"]),
            (CheckLineEnum.Connection, ["warehouse", "outpost-read"]),
            (CheckLineEnum.Subscriptions, ["accounting"]),
            (CheckLineEnum.ReadRights, ["accounting"]),
            (CheckLineEnum.ReadRights, ["warehouse"]),
            (CheckLineEnum.Server, []),
            (CheckLineEnum.ClockSkew, []),
            (CheckLineEnum.WorkerAccount, ["accounting", CheckedMachine.WorkerAccount]),
            (CheckLineEnum.AgentRights, []),
            (CheckLineEnum.WorkerStart, ["accounting", CheckedMachine.WorkerAccount]),
            (CheckLineEnum.AccessLists, [TestMachine.DataFolder]),
            (CheckLineEnum.AccessLists, [TestMachine.ProgramFolder]),
            (CheckLineEnum.AccessLists, [CheckedMachine.ConnectorFolder]),
            (CheckLineEnum.Files, [AgentFolders.PolicyName]),
            (CheckLineEnum.Files, [AgentFolders.PairingName]),
            (CheckLineEnum.Files, [AgentFolders.SecretsName]),
        ];
        Assert.True(
            expected.Length == run.Rows.Count,
            $"The table has {run.Rows.Count} rows, not {expected.Length}:\n{string.Join("\n", run.Rows)}");

        for (int index = 0; index < expected.Length; index++)
        {
            (CheckLineEnum kind, string[] subject) = expected[index];

            Assert.True(
                CheckTable.Names(run.Rows[index], kind.ToString(), subject),
                $"Row {index + 1} is {run.Rows[index]}, not {kind} {string.Join(" ", subject)}.");
        }
    }

    // The passed reasons of the brief's table, word for word where the brief gives them whole.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Each_line_that_passes_gives_its_reason()
    {
        CheckRun run = await _checked.RunAsync();

        string connector = FakeConnectors.PathOf(ConnectorBitnessEnum.X64, CheckedMachine.ConnectorVersion);
        Assert.Equal(
            $"64-bit: {connector}, {CheckedMachine.ConnectorVersion}, is registered; the workers start with it",
            run.Passed(CheckLineEnum.Connector));
        Assert.Equal("opened as outpost-read (Outpost read)", run.Passed(CheckLineEnum.Connection, "accounting", "outpost-read"));
        Assert.Equal("opened as outpost-write (Outpost write)", run.Passed(CheckLineEnum.Connection, "accounting", "outpost-write"));
        Assert.Equal("opened as outpost-read (Outpost read)", run.Passed(CheckLineEnum.Connection, "warehouse", "outpost-read"));
        Assert.Equal(
            "every event subscription that an external connection can raise has its common module available to it",
            run.Passed(CheckLineEnum.Subscriptions, "accounting"));
        Assert.Equal("outpost-read holds no right to change data", run.Passed(CheckLineEnum.ReadRights, "accounting"));
        Assert.Equal("outpost-read holds no right to change data", run.Passed(CheckLineEnum.ReadRights, "warehouse"));
        Assert.Equal(
            $"{CheckedMachine.Server} confirmed an outcome of a made-up command from {CheckedMachine.Agent}: it is reachable and accepts the agent's key",
            run.Passed(CheckLineEnum.Server));
        Assert.Equal(
            "outpost-worker logs on for a batch job, and its token is an ordinary user's",
            run.Passed(CheckLineEnum.WorkerAccount, "accounting", CheckedMachine.WorkerAccount));
        Assert.EndsWith(
            " holds \"Replace a process level token\" and \"Adjust memory quotas for a process\"",
            run.Passed(CheckLineEnum.AgentRights),
            StringComparison.Ordinal);
        Assert.Equal(
            $"the worker started under outpost-worker on {FakeWorkerAccount.DesktopOf("accounting")} and ended at the end of its input",
            run.Passed(CheckLineEnum.WorkerStart, "accounting", CheckedMachine.WorkerAccount));
        Assert.Equal("admits nobody else, nor do its 3 files", run.Passed(CheckLineEnum.AccessLists, TestMachine.DataFolder));
        Assert.Equal("nobody else may change it", run.Passed(CheckLineEnum.AccessLists, TestMachine.ProgramFolder));
        Assert.Equal("nobody else may change it", run.Passed(CheckLineEnum.AccessLists, CheckedMachine.ConnectorFolder));
        Assert.Equal("2 bases", run.Passed(CheckLineEnum.Files, AgentFolders.PolicyName));
        Assert.Equal(
            $"paired with {CheckedMachine.Server} as {CheckedMachine.Agent}",
            run.Passed(CheckLineEnum.Files, AgentFolders.PairingName));
        Assert.Equal(
            "decrypts and holds every password the policy needs",
            run.Passed(CheckLineEnum.Files, AgentFolders.SecretsName));

        // The stub answered at once at the time of this machine's clock: a skew of 0 ms, judged within a millisecond.
        string skew = run.Passed(CheckLineEnum.ClockSkew);
        Assert.StartsWith("the server's clock is ", skew, StringComparison.Ordinal);
        Assert.EndsWith(" by 0 ms ± 1 ms, within 00:02:00", skew, StringComparison.Ordinal);
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Check_takes_its_steps_in_their_order()
    {
        await _checked.RunAsync();

        AssertInOrder(
            Machine.Operations,
            "find",
            "others \"\"",
            $"writers {TestMachine.ProgramFolder}",
            $"held {TestMachine.AgentSid}",
            "logon accounting outpost-worker",
            "token accounting outpost-worker",
            "probe accounting outpost-worker ",
            "start accounting ",
            "start warehouse ",
            "request outcome");
    }

    // The protocol (§6.2): a poll of check would replace the poll that the running service holds, so check sends none,
    // and reaches the server by one outcome alone.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Check_sends_no_poll_so_the_newest_poll_of_the_running_service_is_not_replaced()
    {
        await _checked.RunAsync();

        Assert.Equal(0, _checked.Stub.Count(OutpostRequestKindEnum.Poll));
        Assert.Equal(1, _checked.Stub.Count(OutpostRequestKindEnum.Outcome));
        Assert.DoesNotContain("request poll", Machine.Operations);
    }

    // check works while the service runs, which holds the audit log and writes the outbox: it changes no file of the data
    // folder, takes no lock there, and changes nothing else of the machine, neither a right, a folder nor the service.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Check_only_reads_the_data_folder_and_changes_nothing_of_the_machine()
    {
        string outcome = $"{Guid.NewGuid()}.json";
        Machine.Data.Audit.Seed("2026-11-21.jsonl", "{}\n");
        Machine.Data.Outcomes.Seed(outcome, "{}");
        string[] files = [AgentFolders.PolicyName, AgentFolders.PairingName, AgentFolders.SecretsName];
        byte[][] before = [.. files.Select(Machine.Data.Root.Bytes)];

        await _checked.RunAsync();

        string[] changes = ["append ", "cut ", "replace ", "delete ", "own ", "apply ", "grant ", "revoke ", "install ", "stop ", "regsvr32 "];
        Assert.DoesNotContain(Machine.Operations, line => changes.Any(change => line.StartsWith(change, StringComparison.Ordinal)));
        Assert.Equal(before, [.. files.Select(Machine.Data.Root.Bytes)]);

        // A lock leaves the file it takes, so none was taken.
        Assert.Equal(files.Order(StringComparer.Ordinal), Machine.Data.Root.Names().Order(StringComparer.Ordinal));
        Assert.Equal(["2026-11-21.jsonl"], Machine.Data.Audit.Names());
        Assert.Equal([outcome], Machine.Data.Outcomes.Names());
    }

    // check runs Reads of its own scripts alone (the S10b brief, "The connections"), every worker in safe mode whatever
    // the policy says of the Writes of its base, such as accounting's without safe mode.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Every_worker_runs_its_scripts_in_read_mode_and_in_safe_mode_with_the_account_of_the_policy_and_its_password()
    {
        await _checked.RunAsync();

        Assert.Equal(3, Machine.Workers.Processes.Count);
        Assert.All(Machine.Workers.Processes, process =>
        {
            Assert.Equal(CheckedMachine.Program, process.Program);
            Assert.NotNull(process.Connection);
            Assert.True(process.Connection.SafeMode);
            Assert.Equal(CheckedMachine.Executor, process.Connection.Executor);
            Assert.All(process.Commands, command =>
            {
                Assert.Equal("{}", command.ParametersJson);
                Assert.Null(command.CallerKey);
                Assert.Equal(OutpostProtocol.MaxOutcomeSizeLimit, command.OutcomeLimit);
            });
        });

        Assert.Equal(
            [
                ("accounting", "Srvr=\"srv-1c:1541\";Ref=\"accounting\";", "outpost-read", CheckedMachine.ReadPassword),
                ("accounting", "Srvr=\"srv-1c:1541\";Ref=\"accounting\";", "outpost-write", CheckedMachine.WritePassword),
                ("warehouse", "File=\"D:\\1C\\Warehouse\";", "outpost-read", CheckedMachine.WarehousePassword),
            ],
            Machine.Workers.Processes.Select(process =>
                (process.Base, process.Connection!.Location, process.Connection.User, process.Connection.Password)));
    }

    // The read account runs the scripts of its user, of the subscriptions for a base with write, and of its rights; the
    // write account the first alone.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_read_account_runs_the_scripts_of_its_user_its_subscriptions_and_its_rights_and_the_write_account_that_of_its_user()
    {
        await _checked.RunAsync();

        Assert.Equal(
            [
                ("accounting", "outpost-read", "user subscriptions rights"),
                ("accounting", "outpost-write", "user"),
                ("warehouse", "outpost-read", "user rights"),
            ],
            Machine.Workers.Processes.Select(process => (
                process.Base,
                process.Connection!.User,
                string.Join(" ", process.Commands.Select(command => FakeCheckProcess.KindOf(command.Script))))));
    }

    // The service runs every worker of a base with a worker account under it (D12), so check does as well, through the
    // Secondary Logon service; a base without one under the console's own token.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task The_workers_of_a_base_with_a_worker_account_run_under_it_and_those_of_a_base_without_one_under_the_console()
    {
        await _checked.RunAsync();

        Assert.Equal(
            [("accounting", "outpost-worker"), ("accounting", "outpost-worker"), ("warehouse", null)],
            Machine.Workers.Processes.Select(process => (process.Base, process.Account)));
    }

    // Each failed logon counts toward a lockout of the account, so check logs each worker account on once per run, for
    // its line, its probe and the workers of its base, with the password of secrets.dat.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Each_worker_account_logs_on_once_per_run_with_the_password_of_secrets_dat()
    {
        await _checked.RunAsync();

        Assert.Equal([("accounting", "outpost-worker", CheckedMachine.WorkerPassword)], Machine.Workers.LogOns);
        Assert.Single(Machine.Operations, line => line.StartsWith("logon ", StringComparison.Ordinal));
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task Every_worker_and_every_worker_account_of_check_is_closed_by_its_end()
    {
        await _checked.RunAsync();

        Assert.All(Machine.Workers.Processes, process => Assert.True(process.IsDisposed, $"The worker of {process.Base} was not disposed."));
        Assert.All(Machine.Workers.Accounts, account => Assert.True(account.IsDisposed, $"{account.Account} was not disposed."));
    }

    // The steps in the order given, each line named by its start, among the other operations.
    private static void AssertInOrder(IReadOnlyList<string> operations, params string[] starts)
    {
        int from = 0;

        foreach (string start in starts)
        {
            int index = -1;

            for (int position = from; position < operations.Count; position++)
            {
                if (operations[position].StartsWith(start, StringComparison.Ordinal))
                {
                    index = position;
                    break;
                }
            }

            Assert.True(index >= 0, $"No operation \"{start}…\" after the one before it:\n{string.Join("\n", operations)}");
            from = index + 1;
        }
    }
}
