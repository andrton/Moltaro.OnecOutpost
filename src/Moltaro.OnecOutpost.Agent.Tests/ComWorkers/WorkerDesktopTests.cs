using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The desktop of a worker (the S9c brief, "The desktop of a worker"): a logged-on base gets a desktop of the agent's
// own, OnecOutpost-{base key}-{logon} on the agent's own non-interactive window station, which admits the logon of the
// base's token and nobody else but SYSTEM and Administrators, and every start of the base names it in lpDesktop; the
// station gains one entry for that logon. A .NET program, the worker and the test program alike, connects to its
// station and desktop before Main, so one that they do not admit ends at once and never writes its ready line. The
// tests reach the desktop through the seam that LogOn uses, WorkerDesktop.Make and ComWorkerLauncher.Use, with the
// token of the process of the tests in place of a worker account's, since no test logs an account on; a second logon
// session is the lab's. That the lists admit that one logon and nobody else is shown by reading them back: a start
// that they refuse cannot tell the desktop's list from the station's.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
[Collection(WindowStationCollection.Name)]
public sealed class WorkerDesktopTests : IDisposable
{
    private const string BaseKey = "accounting";

    // What the station's entry for the logon gives: WINSTA_ENUMDESKTOPS, WINSTA_READATTRIBUTES, WINSTA_ENUMERATE and
    // WINSTA_ACCESSGLOBALATOMS.
    private const int StationRightsOfTheLogon = 0x123;

    private readonly TestConnector _connector = TestConnector.Register(RegistryView.Registry64);

    private readonly WorkerJob _job = new();

    public void Dispose()
    {
        _job.Dispose();
        _connector.Dispose();
    }

    // The worker account is never granted WinSta0 or its Default desktop: the station is the agent's own
    // non-interactive one, which Windows names Service-0x{high}-{low}$ after the agent's logon session.
    [Fact]
    public void The_desktop_of_a_base_is_on_the_agents_own_non_interactive_station_never_WinSta0()
    {
        using SafeAccessTokenHandle token = TestTokens.Own();
        using WorkerDesktop desktop = WorkerDesktop.Make(BaseKey, token);

        Assert.StartsWith("Service-0x", desktop.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("WinSta0", desktop.Name, StringComparison.OrdinalIgnoreCase);
    }

    // One desktop per logon (the coordinator, from the review of the desktop, round 1): {logon} is the identifier of the
    // token's logon session in hexadecimal, so that no desktop of an earlier logon is ever given again.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void The_desktop_of_a_base_is_named_after_the_base_and_the_logon_of_its_token()
    {
        using SafeAccessTokenHandle token = TestTokens.Own();
        using WorkerDesktop desktop = WorkerDesktop.Make(BaseKey, token);

        Assert.EndsWith($@"\OnecOutpost-{BaseKey}-{TestTokens.LogonOf(token):x}", desktop.Name, StringComparison.Ordinal);
    }

    // The brief's list, protected and inherited by nothing: GA, which Windows maps when it stores an entry, reads back
    // as the full mask of a desktop, 0xF01FF; the logon gets 0xC7, no hook, journal or switch right.
    [Fact]
    public void The_desktop_admits_the_logon_of_its_token_SYSTEM_and_Administrators_and_nobody_else()
    {
        using SafeAccessTokenHandle token = TestTokens.Own();
        SecurityIdentifier logon = TestTokens.LogonSidOf(token);
        using WorkerDesktop desktop = WorkerDesktop.Make(BaseKey, token);

        string expected = $"D:P(A;;0xf01ff;;;SY)(A;;0xf01ff;;;BA)(A;;0xc7;;;{logon.Value})";

        Assert.Equal(Normalized(expected), Normalized(TestDesktop.DesktopDacl(desktop.Name)));
    }

    // The station gains one allow entry of 0x123 for the logon, inherited by nothing, and nothing else changes. The
    // station lives as long as the logon session of the tests, and keeps the entries of every Make of that session, so
    // an earlier Make may have added this one already; the list read before Make, without that entry, is the list
    // after it, without that entry.
    [Fact]
    public void The_station_gains_one_entry_of_0x123_for_the_logon_of_the_token_and_nothing_else()
    {
        using SafeAccessTokenHandle token = TestTokens.Own();
        SecurityIdentifier logon = TestTokens.LogonSidOf(token);
        List<string> before = Entries(TestDesktop.StationDacl());

        using WorkerDesktop desktop = WorkerDesktop.Make(BaseKey, token);

        List<string> after = Entries(TestDesktop.StationDacl());
        string entry = Entry(AceQualifier.AccessAllowed, AceFlags.None, logon, StationRightsOfTheLogon);

        Assert.Single(after, given => given == entry);
        Assert.Equal(before.Where(given => given != entry), after.Where(given => given != entry));
    }

    // The launcher owns the token and the desktop that Use gets, and closes them when it is disposed.
    [Fact]
    public async Task A_worker_of_a_base_given_a_desktop_that_admits_its_token_runs_on_that_desktop()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        SafeAccessTokenHandle token = TestTokens.Own();
        WorkerDesktop desktop = WorkerDesktop.Make(BaseKey, token);
        string name = desktop.Name;
        using ComWorkerLauncher launcher = Launcher();
        launcher.Use(BaseKey, token, desktop);

        using IWorkerProcess worker = launcher.Start(BaseKey, OutpostModeEnum.Read);
        Assert.True(await worker.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
        JsonElement given = TestLines.ResultOf(
            await worker.RunAsync(TestLines.Command("desktop"), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));

        Assert.Equal(name, given.GetString(), ignoreCase: true);
    }

    // The desktop admits the logon SID of the token it was made for, SYSTEM and Administrators. The token given to Use
    // is a restricted version of that token that holds its logon SID and Administrators for deny only, so that neither
    // the desktop nor the station admits it, whether the tests run elevated or not: the worker starts, ends before its
    // first line, and its open gives false. The tests above read the lists themselves.
    [Fact]
    public async Task A_worker_of_a_base_given_a_desktop_that_does_not_admit_its_token_does_not_open()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using SafeAccessTokenHandle own = TestTokens.Own();
        WorkerDesktop desktop = WorkerDesktop.Make(BaseKey, own);
        SafeAccessTokenHandle refused = TestTokens.WithDenyOnly(
            own,
            TestTokens.LogonSidOf(own),
            new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null));
        using ComWorkerLauncher launcher = Launcher();
        launcher.Use(BaseKey, refused, desktop);

        using IWorkerProcess worker = launcher.Start(BaseKey, OutpostModeEnum.Read);

        Assert.IsType<ComWorkerProcess>(worker);
        Assert.False(await worker.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
    }

    private ComWorkerLauncher Launcher()
    {
        return new ComWorkerLauncher(TestProgram.Path, TestProgram.Path, _connector.ProgId, _job);
    }

    // A DACL in the form in which Windows writes it, so that two lists of the same entries read the same.
    private static string Normalized(string dacl)
    {
        return new RawSecurityDescriptor(dacl).GetSddlForm(AccessControlSections.Access);
    }

    // The entries of a DACL, each as its kind, flags, account and mask, in their order.
    private static List<string> Entries(string dacl)
    {
        RawAcl acl = new RawSecurityDescriptor(dacl).DiscretionaryAcl!;
        List<string> entries = [];

        foreach (GenericAce ace in acl)
        {
            entries.Add(ace is CommonAce common
                ? Entry(common.AceQualifier, common.AceFlags, common.SecurityIdentifier, common.AccessMask)
                : $"{ace.AceType} {ace.AceFlags}");
        }

        return entries;
    }

    private static string Entry(AceQualifier qualifier, AceFlags flags, SecurityIdentifier account, int mask)
    {
        return $"{qualifier} {flags} {account.Value} 0x{mask:x}";
    }
}
