using System.ComponentModel;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Tests.FakeHttp;
using Moltaro.OnecOutpost.Agent.Tests.FakeWindows;
using Moltaro.OnecOutpost.Agent.Tests.RunningAgent;
using Moltaro.OnecOutpost.Agent.Tests.Start;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// The logon of a worker account by the real launcher (the S9c brief, "ComWorkerLauncher.LogOn"): LogonUserW comes
// first, so that an account that does not exist fails with 1326, ERROR_LOGON_FAILURE, whatever rights the agent holds,
// and the failure is an AgentStopException that names the base, the account and the message of Windows, never the
// password. The account is a random name that no machine has, never the account that runs the tests, so no account
// is created, changed or locked out; its password is random too.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerLogOnTests : IDisposable
{
    // A ProgID that no class has: no worker starts in these tests.
    private static readonly string NoConnector = $"Moltaro.OnecOutpost.NoConnector.{Guid.NewGuid():N}";

    private readonly DiskDataFolder _data = new();

    // 20 characters, the longest a worker account may have.
    private readonly string _account = $"outpost-{Guid.NewGuid():N}"[..20];

    private readonly string _password = $"S3cret-{Guid.NewGuid():N}";

    public void Dispose()
    {
        _data.Dispose();
    }

    [Fact]
    public void The_logon_of_an_account_that_does_not_exist_fails_naming_the_base_the_account_and_the_message_of_1326()
    {
        using WorkerJob job = new();
        using ComWorkerLauncher launcher = new(TestProgram.Path, TestProgram.Path, NoConnector, job);

        AgentStopException exception = Assert.Throws<AgentStopException>(() => launcher.LogOn("accounting", _account, _password));

        Assert.Equal(
            $"the worker account {_account} of the base accounting cannot log on: {new Win32Exception(1326).Message}",
            exception.Message);
        Assert.DoesNotContain(_password, exception.Message, StringComparison.Ordinal);
    }

    // The start on a data folder on the disk, with the secrets encrypted by DPAPI for the machine, and the fake access
    // lists, which admit the folder and let nobody else change the program folder: the agent stops before any worker
    // starts, records the reason of the logon in started, polls nothing, and no file of its data folder holds the
    // password in plaintext.
    [Fact]
    public async Task A_start_whose_worker_account_cannot_log_on_stops_the_agent_recorded_and_no_data_file_holds_the_password()
    {
        string policy = PolicyWithWorkerAccount(_account);
        Assert.Contains(_account, policy, StringComparison.Ordinal);
        _data.WriteFiles(policy, ("accounting", _password));
        FakeClock clock = new(TestTimes.Start);
        using ECDsa serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        FakeServer server = new(serverKey, clock);
        using WorkerJob job = new();
        using ComWorkerLauncher launcher = new(TestProgram.Path, TestProgram.Path, NoConnector, job);
        AgentParts parts = new(
            _data.Folders,
            new FakeAccessLists(),
            new MachineDpapi(),
            launcher,
            PairedFolder.Programs,
            TestHost.Executor,
            (_, _) => server,
            clock,
            PairedFolder.Version);

        await using (HostRun run = HostRun.Start(parts))
        {
            Assert.Equal(AgentExitCodes.Error, await run.ExitAsync());
        }

        JsonElement started = _data.LastEntry(TestTimes.Start);
        Assert.Equal("started", started.GetProperty("entry").GetString());
        Assert.Equal(
            $"the worker account {_account} of the base accounting cannot log on: {new Win32Exception(1326).Message}",
            started.GetProperty("error").GetString());
        Assert.Equal(0, server.Count(OutpostRequestKindEnum.Poll));

        foreach ((string path, byte[] bytes) in _data.Files())
        {
            Assert.True(bytes.AsSpan().IndexOf(Encoding.UTF8.GetBytes(_password)) < 0, $"{path} holds the password.");
            Assert.True(bytes.AsSpan().IndexOf(Encoding.Unicode.GetBytes(_password)) < 0, $"{path} holds the password.");
        }
    }

    // PairedFolder's policy, with the Writes of accounting out of safe mode under the account.
    private static string PolicyWithWorkerAccount(string account)
    {
        return PairedFolder.Policy.Replace(
            "\"write\": { \"account\": \"outpost-write\" }",
            $"\"write\": {{ \"account\": \"outpost-write\", \"safeMode\": false, \"workerAccount\": \"{account}\" }}",
            StringComparison.Ordinal);
    }
}
