using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text.Json;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;
using Moltaro.OnecOutpost.TestSupport;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// How the launcher creates a worker (the S9c brief, "The start"): with the token of the base's worker account when the
// base was logged on, and otherwise with the agent's own, which is the account that runs the tests here; suspended, so
// that the worker is in the job before any of its code runs; and with a list of the handles it inherits that holds the
// two ends of its pipes alone, so that it inherits nothing else of the agent. The test program says whether it was in
// a job when its Main began, under which account it runs, and what a handle of a value names in it. No test here logs
// on: S9b's launcher tests run on the agent's own token as well.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerCreationTests : IDisposable
{
    private readonly TestConnector _connector = TestConnector.Register(RegistryView.Registry64);

    private readonly WorkerJob _job = new();

    private readonly TemporaryFolder _folder = new();

    public void Dispose()
    {
        _job.Dispose();
        _connector.Dispose();
        _folder.Dispose();
    }

    // The agent's own token runs only the bases without a worker account.
    [Fact]
    public async Task A_worker_of_a_base_that_was_not_logged_on_runs_under_the_account_of_the_agent()
    {
        using WindowsIdentity agent = WindowsIdentity.GetCurrent();
        using IWorkerProcess worker = await OpenAsync(Launcher());

        JsonElement user = await RunAsync(worker, "user");

        Assert.Equal(agent.User!.Value, user.GetString());
    }

    // S9b's limitation goes: a worker joins the job before it runs, so that no worker outlives an agent that ended
    // between its start and its join.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_worker_is_in_the_job_before_its_program_runs()
    {
        using IWorkerProcess worker = await OpenAsync(Launcher());

        JsonElement inJob = await RunAsync(worker, "job");

        Assert.True(inJob.GetBoolean());
    }

    // A handle of the agent's own that may be inherited, such as one that another part of the agent opened so, reaches
    // no worker. The control shows that the test program sees such a handle when it is inherited: a start as S9b's, by
    // Process.Start with its standard streams redirected, lets the process inherit every handle that may be inherited.
    [Fact(Skip = SkipReason.NotImplemented)]
    public async Task A_worker_inherits_no_handle_of_the_agent_besides_the_ends_of_its_two_pipes()
    {
        string name = $"inheritable-{Guid.NewGuid():N}.txt";
        string path = _folder.PathOf(name);
        File.WriteAllText(path, "");

        using FileStream inheritable = new(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete | FileShare.Inheritable);
        string script = $"handle {inheritable.SafeFileHandle.DangerousGetHandle().ToInt64()}";

        using (StartedWorker control = await StartedWorker.OpenAsync())
        {
            Assert.EndsWith(name, (await RunAsync(control.Worker, script)).GetString(), StringComparison.OrdinalIgnoreCase);
        }

        using IWorkerProcess worker = await OpenAsync(Launcher());
        JsonElement named = await RunAsync(worker, script);

        Assert.True(
            named.ValueKind == JsonValueKind.Null || !named.GetString()!.EndsWith(name, StringComparison.OrdinalIgnoreCase),
            $"The worker inherited {named}.");
    }

    private ComWorkerLauncher Launcher()
    {
        return new ComWorkerLauncher(TestProgram.Path, TestProgram.Path, _connector.ProgId, _job);
    }

    // A worker of accounting for Read, started by the launcher, whose open gave true.
    private static async Task<IWorkerProcess> OpenAsync(ComWorkerLauncher launcher)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        IWorkerProcess worker = launcher.Start("accounting", OutpostModeEnum.Read);

        try
        {
            Assert.True(await worker.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));

            return worker;
        }
        catch
        {
            worker.Dispose();
            throw;
        }
    }

    // The result that the script of the test program gives.
    private static async Task<JsonElement> RunAsync(IWorkerProcess worker, string script)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;

        return TestLines.ResultOf(
            await worker.RunAsync(TestLines.Command(script), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
    }
}
