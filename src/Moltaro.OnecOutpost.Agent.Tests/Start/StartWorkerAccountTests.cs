using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;
using static Moltaro.OnecOutpost.Agent.Tests.AuditEntries;

namespace Moltaro.OnecOutpost.Agent.Tests.Start;

// What S9c adds to a start (the S9c brief, "AgentStart.Open"), with S6b's fake launcher and the fake access lists:
// - after the access lists of the data folder, those of the program folder it is given: the first entry through which
//   somebody else could change the code that the agent's account runs stops the start, naming its path and its
//   accounts, and lists that cannot be read stop it too;
// - with the passwords of the policy, one for the worker account of each base that names one;
// - last, the logon of the worker account of each such base, in the policy's order and for no other base, before
//   started; a logon that fails stops the start with its reason, and no later base is logged on.
// The reasons of the brief are asserted word for word.
public sealed class StartWorkerAccountTests
{
    // PairedFolder's policy with the Writes of accounting out of safe mode under the account outpost-worker.
    private const string Policy = """
        {
          "bases": [
            {
              "key": "accounting",
              "server": "srv-1c:1541",
              "database": "accounting",
              "read": { "account": "outpost-read" },
              "write": { "account": "outpost-write", "safeMode": false, "workerAccount": "outpost-worker" }
            },
            {
              "key": "warehouse",
              "file": "D:\\1C\\Warehouse",
              "read": { "account": "outpost-read" }
            }
          ]
        }
        """;

    // Four bases in this order: two with worker accounts of their own, one only read and one written in safe mode.
    private const string FourBases = """
        {
          "bases": [
            { "key": "payroll", "file": "D:\\1C\\Payroll", "read": { "account": "r" }, "write": { "account": "w", "safeMode": false, "workerAccount": "outpost-payroll" } },
            { "key": "warehouse", "file": "D:\\1C\\Warehouse", "read": { "account": "r" } },
            { "key": "accounting", "file": "D:\\1C\\Accounting", "read": { "account": "r" }, "write": { "account": "w", "safeMode": false, "workerAccount": "outpost-accounting" } },
            { "key": "stock", "file": "D:\\1C\\Stock", "read": { "account": "r" }, "write": { "account": "w", "safeMode": true } }
          ]
        }
        """;

    private const string ExpectedNobodyElse =
        "; expected nobody but Administrators, SYSTEM, TrustedInstaller and the agent's account, as the console's install sets it";

    private static readonly (string Base, OutpostModeEnum Mode, string Password)[] FourBasesPasswords =
    [
        ("payroll", OutpostModeEnum.Read, "pw"),
        ("payroll", OutpostModeEnum.Write, "pw"),
        ("warehouse", OutpostModeEnum.Read, "pw"),
        ("accounting", OutpostModeEnum.Read, "pw"),
        ("accounting", OutpostModeEnum.Write, "pw"),
        ("stock", OutpostModeEnum.Read, "pw"),
        ("stock", OutpostModeEnum.Write, "pw"),
    ];

    private readonly PairedFolder _folder = PairedFolder.Create();

    private readonly FakeClock _clock = new(TestTimes.Start);

    [Fact]
    public void A_start_logs_on_the_worker_account_of_each_base_that_names_one_in_the_order_of_the_policy_before_started()
    {
        _folder.Data.Root.Seed("policy.json", FourBases);
        _folder.SeedSecrets(FourBasesPasswords, [("payroll", "payroll-pw"), ("accounting", "accounting-pw")]);
        int count = _folder.Data.Changes.Count;

        _folder.Start(_clock).Log.Dispose();

        Assert.Equal(
            [("payroll", "outpost-payroll", "payroll-pw"), ("accounting", "outpost-accounting", "accounting-pw")],
            _folder.Workers.LogOns);
        Operations.AssertInOrder(
            Operations.Since(_folder.Data, count),
            "logon payroll outpost-payroll",
            "logon accounting outpost-accounting",
            "started");
    }

    // The console's secret writes the password of the worker account; a start without one would fail every worker of
    // the base.
    [Fact]
    public void A_base_whose_worker_account_has_no_password_in_the_secrets_stops_the_start_naming_the_base()
    {
        _folder.Data.Root.Seed("policy.json", Policy);
        _folder.SeedSecrets(PairedFolder.Passwords);

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Equal(
            "secrets.dat holds no password for the worker account of the base accounting; write it with the console",
            StoppedStart.Error(entries));
        Assert.Empty(_folder.Workers.LogOns);
    }

    // The launcher's AgentStopException is the reason of the start; the first base that cannot log on ends the logons.
    [Fact]
    public void A_logon_that_fails_stops_the_start_with_its_reason_and_no_later_base_is_logged_on()
    {
        const string Reason = "the worker account outpost-payroll of the base payroll cannot log on: refused by the test";

        _folder.Data.Root.Seed("policy.json", FourBases);
        _folder.SeedSecrets(FourBasesPasswords, [("payroll", "payroll-pw"), ("accounting", "accounting-pw")]);
        _folder.Workers.RefuseLogOn("payroll", Reason);

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Equal(Reason, StoppedStart.Error(entries));
        Assert.Equal([("payroll", "outpost-payroll", "payroll-pw")], _folder.Workers.LogOns);
    }

    [Fact]
    public void A_start_asks_whether_anybody_else_may_change_the_program_folder_it_is_given()
    {
        _folder.Start(_clock).Log.Dispose();

        Assert.Equal([PairedFolder.ProgramFolder], _folder.Access.ProgramsAsked);
    }

    // Somebody who could replace the code that the agent's account runs would get its secrets.
    [Fact]
    public void A_program_folder_that_somebody_else_may_change_stops_the_start_naming_the_first_entry_and_its_accounts()
    {
        const string Worker = @"C:\Program Files\Moltaro Outpost\x64\Moltaro.OnecOutpost.ComWorker.exe";
        const string Executor = @"C:\Program Files\Moltaro Outpost\OutpostExecutor.epf";
        _folder.Access.LetChange(Worker, @"PC-1\intruder", @"BUILTIN\Users");
        _folder.Access.LetChange(Executor, @"PC-1\other");

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Single(entries);
        Assert.Equal(
            $@"the access list of {Worker} lets PC-1\intruder, BUILTIN\Users change it" + ExpectedNobodyElse,
            StoppedStart.Error(entries));
    }

    [Fact]
    public void Access_lists_of_the_program_folder_that_cannot_be_read_stop_the_start()
    {
        _folder.Access.MakeProgramsUnreadable();

        List<JsonElement> entries = StoppedStart.Of(_folder, _clock);

        Assert.Single(entries);
        Assert.Equal("the access lists of the program folder cannot be read", StoppedStart.Error(entries));
    }

    // The order of the steps: the data folder's lists come before the program folder's, and those before agent.json.
    [Fact]
    public void The_program_folder_is_judged_after_the_data_folder_and_before_agent_json()
    {
        const string Worker = @"C:\Program Files\Moltaro Outpost\x64\Moltaro.OnecOutpost.ComWorker.exe";
        _folder.Access.Admit("", @"PC-1\intruder");
        _folder.Access.LetChange(Worker, @"PC-1\other");

        string error = StoppedStart.Error(StoppedStart.Of(_folder, _clock));

        Assert.Contains(@"PC-1\intruder", error, StringComparison.Ordinal);
        Assert.DoesNotContain(Worker, error, StringComparison.Ordinal);

        _folder.Access.Fix();
        _folder.Access.LetChange(Worker, @"PC-1\other");
        _folder.Data.Root.Unseed("agent.json");

        error = StoppedStart.Error(StoppedStart.Of(_folder, _clock));

        Assert.Contains(Worker, error, StringComparison.Ordinal);
        Assert.DoesNotContain("agent.json", error, StringComparison.Ordinal);
    }

    // The bases written in safe mode and those only read have no worker account, and nothing logs them on.
    [Fact]
    public void A_start_of_a_policy_without_worker_accounts_logs_nobody_on()
    {
        _folder.Start(_clock).Log.Dispose();

        Assert.Empty(_folder.Workers.LogOns);
        Assert.Equal(["started", "policy"], KindsSince(_folder.Data, 0));
    }
}
