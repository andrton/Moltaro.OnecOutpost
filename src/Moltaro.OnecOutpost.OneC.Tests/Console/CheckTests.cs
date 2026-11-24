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
// nothing of a copy is a literal here. With the write account as the read account, ReadRights names Catalog.Products.
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
        Assert.Contains("NoteBeforeWrite (BeforeWrite) in NoteChecks", subscriptions.Reason, StringComparison.Ordinal);
        Assert.DoesNotContain("NoteFormGet", subscriptions.Reason, StringComparison.Ordinal);
        Assert.All(
            check.Rows.Where(row => row != subscriptions),
            row => Assert.True(row.Passed, $"The line {row} did not pass; the table:\n{string.Join("\n", check.Rows)}"));
        Assert.Equal(ConsoleExitCodes.CheckFailed, check.ExitCode);

        AssertOpenedAs(check, lab.TestBase.Read.User);
        AssertOpenedAs(check, lab.TestBase.Write.User);
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
        check.AssertPassed(About, CheckLineEnum.Connection, LabTestBase.Key, lab.TestBase.Read.User);
        check.AssertPassed(About, CheckLineEnum.Connection, LabTestBase.Key, lab.TestBase.Write.User);
    }

    [Fact]
    public async Task On_each_copy_of_a_typical_base_its_connections_pass_and_every_other_line_is_judged()
    {
        LabFile lab = LabFile.Read();

        Assert.True(lab.TypicalBases.Count > 0, "lab.local.json names no copy of a typical base.");

        for (int index = 0; index < lab.TypicalBases.Count; index++)
        {
            LabTypicalBase typicalBase = lab.TypicalBases[index];
            string about = $"typicalBases[{index}], {typicalBase.Configuration} {typicalBase.Version}";

            using LabAgent agent = typicalBase.WriteSafeMode
                ? LabAgent.InSafeMode(typicalBase)
                : LabAgent.WithoutSafeMode(typicalBase, LabAgent.WorkerAccountOf(lab));

            LabCheck check = await LabConsole.CheckAsync(agent);

            check.AssertPassed(about, CheckLineEnum.Connection, LabTypicalBase.Key, typicalBase.Read.User);
            check.AssertPassed(about, CheckLineEnum.Connection, LabTypicalBase.Key, typicalBase.Write.User);
            Assert.All(check.Rows, row => Assert.False(row.NotChecked, $"{about}: the line {row} was not checked."));
            Assert.Single(check.RowsOf(CheckLineEnum.Subscriptions));
            Assert.Single(check.RowsOf(CheckLineEnum.ReadRights));
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
    private static void AssertOpenedAs(LabCheck check, string user)
    {
        string reason = check.Row(CheckLineEnum.Connection, LabTestBase.Key, user).Reason;

        Assert.StartsWith($"opened as {user} (", reason, StringComparison.OrdinalIgnoreCase);
    }
}
