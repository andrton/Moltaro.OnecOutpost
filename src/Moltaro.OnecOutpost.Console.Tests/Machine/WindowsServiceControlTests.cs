using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Console.Service;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Console.Tests.Machine;

// The service controller of Windows as the console uses it (the S10 brief, "WindowsServiceControl"), with
// Requires=Windows and Requires=Administrator, on a service of the test's own: Install creates the service in its own
// process, starting automatically, under NT SERVICE\{name} without a password, with a description, the failure actions
// of the console, restart after a minute three times with the count reset after a day and not for a stop with an error,
// and no required privileges, which would drop the two of the agent's account; on an existing service it reconfigures
// it, never deleting it, and keeps its failure actions. A service marked for deletion counts as deleted, and cannot be
// installed until it is gone. A stop of a stopped service is Stopped, of an unknown one NotInstalled; stopping a running
// service is the fakes' and the lab's.
[Trait("Requires", "Windows")]
[Trait("Requires", "Administrator")]
[SupportedOSPlatform("windows")]
public sealed class WindowsServiceControlTests
{
    private const string DisplayName = "Moltaro OnecOutpost agent";

    private readonly WindowsServiceControl _services = new(new FakeClock(TestTimes.Start));

    [Fact(Skip = SkipReason.NotImplemented)]
    public void Install_creates_the_service_as_the_agent_runs_and_again_reconfigures_it_keeping_its_failure_actions()
    {
        TestService service = new();

        try
        {
            string binaryPath = BinaryPath(service.ProgramFolder);

            Assert.True(_services.Install(service.Name, DisplayName, binaryPath));

            Assert.Equal((TestService.OwnProcess, TestService.AutomaticStart, binaryPath), Started(service));
            Assert.Equal(service.Account, service.Configuration().Account, ignoreCase: true);
            Assert.Equal(DisplayName, service.Configuration().DisplayName);
            Assert.False(string.IsNullOrEmpty(service.Description()));
            (uint resetPeriod, (int Type, uint Delay)[] actions) = service.FailureActions();
            Assert.Equal(86400u, resetPeriod);
            Assert.Equal([(TestService.Restart, 60000u), (TestService.Restart, 60000u), (TestService.Restart, 60000u)], actions);
            Assert.False(service.FailureActionsOnNonCrashFailures());
            Assert.Empty(service.RequiredPrivileges());

            // An administrator changed the failure actions since, and the program moved.
            service.SetFailureActions(3600, (TestService.Restart, 120000u));
            string movedPath = BinaryPath(Path.Combine(service.ProgramFolder, "moved"));

            Assert.False(_services.Install(service.Name, DisplayName, movedPath));

            Assert.Equal((TestService.OwnProcess, TestService.AutomaticStart, movedPath), Started(service));
            Assert.Equal(service.Account, service.Configuration().Account, ignoreCase: true);
            Assert.False(string.IsNullOrEmpty(service.Description()));
            (resetPeriod, actions) = service.FailureActions();
            Assert.Equal(3600u, resetPeriod);
            Assert.Equal([(TestService.Restart, 120000u)], actions);
            Assert.Empty(service.RequiredPrivileges());
        }
        finally
        {
            service.Remove();
        }
    }

    // 1072: a handle that the test holds, as the Services console or Event Viewer holds one, keeps a deleted service.
    [Fact(Skip = SkipReason.NotImplemented)]
    public void A_service_marked_for_deletion_counts_as_deleted_and_is_not_installed_again_until_it_is_gone()
    {
        TestService service = new();
        nint held = 0;

        try
        {
            _services.Install(service.Name, DisplayName, BinaryPath(service.ProgramFolder));
            held = service.Open();

            _services.Delete(service.Name);
            _services.Delete(service.Name);

            Win32Exception refused = Assert.Throws<Win32Exception>(() =>
                _services.Install(service.Name, DisplayName, BinaryPath(service.ProgramFolder)));
            Assert.Equal(1072, refused.NativeErrorCode);
        }
        finally
        {
            TestService.Close(held);
            service.Remove();
        }
    }

    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_stop_of_a_stopped_service_gives_Stopped_and_of_an_unknown_one_NotInstalled()
    {
        TestService service = new();
        TestService unknown = new();

        try
        {
            _services.Install(service.Name, DisplayName, BinaryPath(service.ProgramFolder));

            Assert.Equal(ServiceStopEnum.Stopped, await StopAsync(service));
            Assert.Equal(ServiceStopEnum.NotInstalled, await StopAsync(unknown));
        }
        finally
        {
            service.Remove();
        }
    }

    // ServiceAccount.Sid computes the SID without Windows, before the service exists (the agent's tests).
    [Fact(Skip = SkipReason.NotImplemented)]
    public void Windows_translates_the_account_of_the_service_it_created_to_the_SID_of_ServiceAccount()
    {
        TestService service = new();

        try
        {
            _services.Install(service.Name, DisplayName, BinaryPath(service.ProgramFolder));

            Assert.Equal(ServiceAccount.Sid(service.Name), new NTAccount(service.Account).Translate(typeof(SecurityIdentifier)).Value);
        }
        finally
        {
            service.Remove();
        }
    }

    private static string BinaryPath(string programFolder)
    {
        return $"\"{Path.Combine(programFolder, "Moltaro.OnecOutpost.Agent.exe")}\" service";
    }

    private static (uint Type, uint Start, string BinaryPath) Started(TestService service)
    {
        (uint type, uint start, string binaryPath, _, _) = service.Configuration();

        return (type, start, binaryPath);
    }

    private Task<ServiceStopEnum> StopAsync(TestService service)
    {
        return TestTimes.EndOf(_services.StopAsync(service.Name, TimeSpan.FromMinutes(1), TestContext.Current.CancellationToken));
    }
}
