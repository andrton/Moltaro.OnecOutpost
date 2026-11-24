using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.OneC.Tests.Agent;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.TestSupport.Check;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// The acceptance of check on the lab (the S10b brief, "Lab tests", and Acceptance), run with S10c: check against the
// test base and against each copy of a typical base of lab.local.json, through the console's own command on the
// machine's parts, with the server's part against a stub of the agent's keys, and no poll. The test base in safe mode
// passes every line but Subscriptions, which names the subscription NoteBeforeWrite of the test configuration, whose
// module NoteChecks an external connection lacks, and not NoteFormGet, a subscription to FormGetProcessing, which no
// external connection raises; the users of its sessions are the accounts of lab.local.json. Under the lab's worker
// account, the account, the agent's rights, the probe and the connections pass. On each copy of a typical base, in
// safe mode or not as its writeSafeMode says, the connections pass and every other line is judged, whatever it finds;
// nothing of a copy is a literal here. The copy's Connection rows name their mode, so that a copy whose two modes share
// one 1C account has a row of each, and each of its scripts gives a result of its form, the subscriptions' too (the S10
// brief, "After the lab run of deeb370", 3 and 5); no message names its account or an object of its configuration.
// With the write account as the read account, ReadRights names Catalog.Products.
[SupportedOSPlatform("windows")]
public sealed class CheckTests
{
    [Fact]
    public async Task On_the_test_base_in_safe_mode_every_line_passes_but_Subscriptions_which_names_NoteBeforeWrite_and_not_NoteFormGet()
    {
        LabFile lab = LabFile.Read();
        using LabAgent agent = LabAgent.Create();

        LabCheck check = await LabConsole.CheckAsync(agent);

        CheckRow subscriptions = check.Row(CheckLineEnum.Subscriptions, LabTestBase.Key);
        Assert.False(subscriptions.Passed, subscriptions.ToString());

        const string EnglishReason = """a Write that reaches these fails: NoteBeforeWrite (BeforeWrite) in NoteChecks; set "External connection" of each module""";
        const string RussianReason = """a Write that reaches these fails: NoteBeforeWrite (ПередЗаписью) in NoteChecks; set "External connection" of each module""";
        Assert.True(
            subscriptions.Reason is EnglishReason or RussianReason,
            $"Expected NoteBeforeWrite with its platform event name in the NoteChecks common module. The lab reported {subscriptions}");

        Assert.All(
            check.Rows.Where(row => row != subscriptions),
            row => Assert.True(row.Passed, $"The line {row} did not pass; the table:\n{string.Join("\n", check.Rows)}"));
        Assert.Equal(ConsoleExitCodes.CheckFailed, check.ExitCode);

        AssertOpenedAs(check, "read", lab.TestBase.Read.User);
        AssertOpenedAs(check, "write", lab.TestBase.Write.User);
        Assert.Empty(check.RowsOf(CheckLineEnum.WorkerAccount));
        Assert.Empty(check.RowsOf(CheckLineEnum.WorkerStart));
    }

    [Fact]
    public async Task Under_the_worker_account_of_the_lab_its_logon_the_agent_rights_the_probe_and_the_connections_pass()
    {
        LabFile lab = LabFile.Read();
        LabAccount workerAccount = LabAgent.WorkerAccountOf(lab);
        using LabAgent agent = LabAgent.WithoutSafeMode(lab.TestBase, workerAccount);

        LabCheck check = await LabConsole.CheckAsync(agent);

        const string About = "the test base under the worker account";
        check.AssertPassed(About, CheckLineEnum.WorkerAccount, LabTestBase.Key, workerAccount.User);
        check.AssertPassed(About, CheckLineEnum.AgentRights);
        check.AssertPassed(About, CheckLineEnum.WorkerStart, LabTestBase.Key, workerAccount.User);
        Assert.StartsWith(
            $"the worker started under {workerAccount.User} on ",
            check.Row(CheckLineEnum.WorkerStart, LabTestBase.Key, workerAccount.User).Reason,
            StringComparison.Ordinal);
        check.AssertPassed(About, CheckLineEnum.Connection, $"{LabTestBase.Key} read {lab.TestBase.Read.User}");
        check.AssertPassed(About, CheckLineEnum.Connection, $"{LabTestBase.Key} write {lab.TestBase.Write.User}");
    }

    [Fact]
    public async Task On_each_copy_of_a_typical_base_its_connections_pass_and_every_other_line_is_judged()
    {
        LabFile lab = LabFile.Read();

        Assert.True(lab.TypicalBases.Count > 0, "lab.local.json names no copy of a typical base.");

        for (int index = 0; index < lab.TypicalBases.Count; index++)
        {
            LabTypicalBase typicalBase = lab.TypicalBases[index];
            string about = $"typicalBases[{index}]";

            using LabAgent agent = typicalBase.WriteSafeMode
                ? LabAgent.InSafeMode(typicalBase)
                : LabAgent.WithoutSafeMode(typicalBase, LabAgent.WorkerAccountOf(lab));

            LabCheck check = await LabConsole.CheckAsync(agent);

            check.AssertPassedQuietly(about, "the connection of the read mode", CheckLineEnum.Connection, $"{LabTypicalBase.Key} read {typicalBase.Read.User}");
            check.AssertPassedQuietly(about, "the connection of the write mode", CheckLineEnum.Connection, $"{LabTypicalBase.Key} write {typicalBase.Write.User}");

            foreach (CheckRow row in check.Rows)
            {
                Assert.False(row.NotChecked, $"{about}: a line {LabCheck.KindOf(row)} was not checked; {check.Summary}");
                Assert.False(
                    row.Reason.StartsWith("the script of the check ", StringComparison.Ordinal),
                    $"{about}: the script of a line {LabCheck.KindOf(row)} did not give a result of its form; {check.Summary}");
            }

            Assert.True(check.RowsOf(CheckLineEnum.Subscriptions).Count == 1, $"{about}: not one line Subscriptions; {check.Summary}");
            Assert.True(check.RowsOf(CheckLineEnum.ReadRights).Count == 1, $"{about}: not one line ReadRights; {check.Summary}");
            TestContext.Current.TestOutputHelper?.WriteLine($"{about}: {check.Summary}");
        }
    }

    // OutpostWrite inserts, updates and deletes the products, and reads Notes alone.
    [Fact]
    public async Task With_the_write_account_as_the_read_account_ReadRights_fails_naming_Catalog_Products()
    {
        LabFile lab = LabFile.Read();
        using LabAgent agent = LabAgent.ReadingAs(lab.TestBase, lab.TestBase.Write);

        LabCheck check = await LabConsole.CheckAsync(agent);

        CheckRow rights = check.Row(CheckLineEnum.ReadRights, LabTestBase.Key);
        Assert.False(rights.Passed, rights.ToString());
        Assert.StartsWith($"{lab.TestBase.Write.User} holds rights to change data: ", rights.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Catalog.Products", rights.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("Catalog.Notes", rights.Reason, StringComparison.Ordinal);
    }

    // The session's user is the account of lab.local.json, as 1C spells it.
    private static void AssertOpenedAs(LabCheck check, string mode, string user)
    {
        string reason = check.Row(CheckLineEnum.Connection, $"{LabTestBase.Key} {mode} {user}").Reason;

        Assert.StartsWith($"opened as {user} (", reason, StringComparison.OrdinalIgnoreCase);
    }
}
