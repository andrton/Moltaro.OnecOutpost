using System.Runtime.Versioning;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.OneC.Tests.Agent;
using Moltaro.OnecOutpost.OneC.Tests.Lab;
using Moltaro.OnecOutpost.TestSupport;
using Moltaro.OnecOutpost.TestSupport.Check;

namespace Moltaro.OnecOutpost.OneC.Tests.Console;

// The lines of the worker account on the lab,
// each failure case broken on purpose where that needs no change of the machine: a changed password of the lab's worker
// account and an account that does not exist fail the logon with 1326; the local Guest, disabled, with a blank password,
// fails it with 1331, or with 1327 when Windows judges the blank password first; and the program folder of the test,
// without the entry of Users, fails the probe with 5 or 267, since the account may not read the program or its folder.
// A failed logon is tried once, so each run counts once toward a lockout of the worker account. The probe and the
// connections of the base are not checked after a failed logon, nor the connections after a failed probe.
[SupportedOSPlatform("windows")]
public sealed class CheckWorkerAccountTests
{
    [Fact]
    public async Task A_changed_password_of_the_worker_account_fails_its_logon_with_1326()
    {
        LabAccount workerAccount = LabAgent.WorkerAccountOf(LabFile.Read());

        LabCheck check = await CheckUnderAsync(new LabAccount { User = workerAccount.User, Password = $"{workerAccount.Password}-changed" });

        AssertCannotLogOn(check, workerAccount.User, 1326);
    }

    [Fact]
    public async Task A_worker_account_that_does_not_exist_fails_its_logon_with_1326()
    {
        string user = $"outpost-{Guid.NewGuid():N}"[..20];

        LabCheck check = await CheckUnderAsync(new LabAccount { User = user, Password = $"S3cret-{Guid.NewGuid():N}" });

        AssertCannotLogOn(check, user, 1326);
    }

    // The lab records whether Windows rejects the disabled account or the blank password first.
    [Fact]
    public async Task The_disabled_Guest_with_a_blank_password_fails_its_logon_as_disabled_or_by_a_restriction_of_the_account()
    {
        string guest = GuestOf(LabAgent.WorkerAccountOf(LabFile.Read()));

        LabCheck check = await CheckUnderAsync(new LabAccount { User = guest, Password = "" });

        CheckRow row = check.Row(CheckLineEnum.WorkerAccount, LabTestBase.Key, guest);
        Assert.False(row.Passed, row.ToString());
        int[] codes = [.. new[] { 1331, 1327 }.Where(code => row.Reason.EndsWith(WorkerAccountReasons.CannotLogOn(LabTestBase.Key, code), StringComparison.Ordinal))];
        Assert.True(codes.Length == 1, $"The logon of {guest} failed with another reason: {row.Reason}");
        TestContext.Current.TestOutputHelper?.WriteLine($"The logon of the disabled Guest with a blank password failed with {codes[0]}.");
        Assert.True(check.Row(CheckLineEnum.WorkerStart, LabTestBase.Key, guest).NotChecked);
    }

    // The test's own program folder, made as the console's install makes it, loses the entry that lets Users read and
    // run the worker; nothing of the machine changes. The program's folder is the worker's working folder. The
    // Secondary Logon service may reject that folder with error 267 or the executable with error 5. The lab records
    // which error Windows returns.
    [Fact]
    public async Task A_program_folder_without_the_entry_of_Users_fails_the_probe_as_a_program_or_a_folder_that_the_account_may_not_read()
    {
        LabFile lab = LabFile.Read();
        LabAccount workerAccount = LabAgent.WorkerAccountOf(lab);
        using LabAgent agent = LabAgent.WithoutSafeMode(lab.TestBase, workerAccount);
        AccessListEdits.AdmitNobodyElse(agent.ProgramsPath);

        LabCheck check = await LabConsole.CheckAsync(agent);

        check.AssertPassed("the worker account", CheckLineEnum.WorkerAccount, LabTestBase.Key, workerAccount.User);
        CheckRow probe = check.Row(CheckLineEnum.WorkerStart, LabTestBase.Key, workerAccount.User);
        Assert.False(probe.Passed, probe.ToString());
        bool program = probe.Reason == $"{workerAccount.User} may not read or run {agent.WorkerPath}";
        Assert.True(
            program || probe.Reason == $"{workerAccount.User} may not read the folder of {agent.WorkerPath}, or it does not exist",
            $"The probe failed with another reason: {probe.Reason}");
        TestContext.Current.TestOutputHelper?.WriteLine(
            $"The probe of a program folder without the entry of Users failed with {(program ? "5, the program" : "267, its folder")}.");
        Assert.True(check.Row(CheckLineEnum.Connection, $"{LabTestBase.Key} read {lab.TestBase.Read.User}").NotChecked);
        Assert.True(check.Row(CheckLineEnum.Connection, $"{LabTestBase.Key} write {lab.TestBase.Write.User}").NotChecked);
    }

    // check of the test base without safe mode under the account given, whose password secrets.dat holds.
    private static async Task<LabCheck> CheckUnderAsync(LabAccount workerAccount)
    {
        using LabAgent agent = LabAgent.WithoutSafeMode(LabFile.Read().TestBase, workerAccount);

        return await LabConsole.CheckAsync(agent);
    }

    // The logon failed with the reason of the code, once, and the base started no worker.
    private static void AssertCannotLogOn(LabCheck check, string user, int error)
    {
        CheckRow row = check.Row(CheckLineEnum.WorkerAccount, LabTestBase.Key, user);

        Assert.False(row.Passed, row.ToString());
        Assert.EndsWith(WorkerAccountReasons.CannotLogOn(LabTestBase.Key, error), row.Reason, StringComparison.Ordinal);
        Assert.True(check.Row(CheckLineEnum.WorkerStart, LabTestBase.Key, user).NotChecked);
        Assert.All(check.RowsOf(CheckLineEnum.Connection), connection => Assert.True(connection.NotChecked, connection.ToString()));
        Assert.Equal(ConsoleExitCodes.CheckFailed, check.ExitCode);
    }

    // The local Guest by its well-known RID in the domain of this machine, that of the lab's worker account, a local
    // user, so that a localized name of the account is found too.
    private static string GuestOf(LabAccount workerAccount)
    {
        SecurityIdentifier local = (SecurityIdentifier)new NTAccount(workerAccount.User).Translate(typeof(SecurityIdentifier));
        SecurityIdentifier guest = new(WellKnownSidType.AccountGuestSid, local.AccountDomainSid);
        string name = guest.Translate(typeof(NTAccount)).Value;

        return name[(name.IndexOf('\\', StringComparison.Ordinal) + 1)..];
    }
}
