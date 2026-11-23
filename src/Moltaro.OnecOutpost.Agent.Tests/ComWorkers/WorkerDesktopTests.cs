using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The desktop of a worker (the S9c brief, "The desktop of a worker"): a logged-on base gets a desktop of the agent's
// own, OnecOutpost-{base key} on the agent's own non-interactive window station, which admits the logon of the base's
// token and nobody else but SYSTEM and Administrators, and every start of the base names it in lpDesktop. A .NET
// program, the worker and the test program alike, connects to its desktop before Main, so one that its desktop does not
// admit ends at once and never writes its ready line. The tests reach the desktop through the seam that LogOn uses,
// WorkerDesktop.Make and ComWorkerLauncher.Use, with the token of the process of the tests in place of a worker
// account's, since no test logs an account on; a second logon session is the lab's.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerDesktopTests : IDisposable
{
    private const string BaseKey = "accounting";

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
        Assert.EndsWith($@"\OnecOutpost-{BaseKey}", desktop.Name, StringComparison.Ordinal);
        Assert.DoesNotContain("WinSta0", desktop.Name, StringComparison.OrdinalIgnoreCase);
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
    // is a restricted version of that token that holds its logon SID and Administrators for deny only, so that the
    // desktop admits nothing of it, whether the tests run elevated or not: the worker starts, ends before its first
    // line, and its open gives false.
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
}
