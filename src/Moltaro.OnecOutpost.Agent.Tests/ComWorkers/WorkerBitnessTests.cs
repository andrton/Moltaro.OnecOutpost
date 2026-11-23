using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Moltaro.OnecOutpost.Agent.Workers;
using Moltaro.OnecOutpost.Protocol.Outcomes;

namespace Moltaro.OnecOutpost.Agent.Tests.ComWorkers;

// Concept §5 and O8, the S9b brief, "The bitness": a COM worker runs with the bitness of the connector it uses. The
// launcher reads the registry at each start and starts the x64 program when the connector is registered for 64 bits,
// otherwise the x86 program when it is for 32 bits, and otherwise no process, whose open gives false; a program that
// does not start gives no process too, and Start throws nothing. The test program is copied into x64\ and x86\ of the
// test's folder, path names the copy that runs, and the class of the connector is the test's own, for the current
// user.
[Trait("Requires", "Windows")]
[SupportedOSPlatform("windows")]
public sealed class WorkerBitnessTests : IDisposable
{
    private readonly TemporaryFolder _folder = new();

    public void Dispose()
    {
        _folder.Dispose();
    }

    [Theory]
    [InlineData("in both views", "x64")]
    [InlineData("only in the view of 32 bits", "x86")]
    public async Task A_worker_runs_the_program_of_the_bitness_for_which_the_connector_is_registered(string registered, string bitness)
    {
        string x64 = TestProgram.CopyInto(_folder.PathOf("x64"));
        string x86 = TestProgram.CopyInto(_folder.PathOf("x86"));
        RegistryView[] views = registered == "in both views"
            ? [RegistryView.Registry64, RegistryView.Registry32]
            : [RegistryView.Registry32];
        using TestConnector connector = TestConnector.Register(views);
        using WorkerJob job = new();
        ComWorkerLauncher launcher = new(x64, x86, connector.ProgId, job);

        string path = await PathOfAStartedWorkerAsync(launcher);

        Assert.Equal(bitness == "x64" ? x64 : x86, path, ignoreCase: true);
    }

    [Theory]
    [InlineData("no connector registered")]
    [InlineData("a program that does not exist")]
    public async Task With_no_connector_registered_or_a_program_that_does_not_exist_the_open_of_a_worker_gives_false(string form)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        bool missingProgram = form == "a program that does not exist";
        string x64 = missingProgram ? _folder.PathOf(@"x64\Moltaro.OnecOutpost.TestWorker.exe") : TestProgram.CopyInto(_folder.PathOf("x64"));
        string x86 = missingProgram ? _folder.PathOf(@"x86\Moltaro.OnecOutpost.TestWorker.exe") : TestProgram.CopyInto(_folder.PathOf("x86"));
        using TestConnector connector = missingProgram
            ? TestConnector.Register(RegistryView.Registry64, RegistryView.Registry32)
            : TestConnector.Register();
        using WorkerJob job = new();
        ComWorkerLauncher launcher = new(x64, x86, connector.ProgId, job);

        using IWorkerProcess worker = launcher.Start("accounting", OutpostModeEnum.Read);

        Assert.False(await worker.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
    }

    // A start that found no connector registered is followed by one that finds the class registered since, as after an
    // administrator registered the connector while the agent ran.
    [Fact]
    public async Task The_launcher_reads_the_registry_at_each_start()
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        string x64 = TestProgram.CopyInto(_folder.PathOf("x64"));
        string x86 = TestProgram.CopyInto(_folder.PathOf("x86"));
        using TestConnector connector = TestConnector.Register();
        using WorkerJob job = new();
        ComWorkerLauncher launcher = new(x64, x86, connector.ProgId, job);

        using (IWorkerProcess none = launcher.Start("accounting", OutpostModeEnum.Read))
        {
            Assert.False(await none.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
        }

        connector.RegisterIn(RegistryView.Registry32);

        Assert.Equal(x86, await PathOfAStartedWorkerAsync(launcher), ignoreCase: true);
    }

    // Starts a worker, gives the path of its program, and ends it before the test's folder goes.
    private static async Task<string> PathOfAStartedWorkerAsync(ComWorkerLauncher launcher)
    {
        CancellationToken cancellationToken = TestContext.Current.CancellationToken;
        using IWorkerProcess worker = launcher.Start("accounting", OutpostModeEnum.Read);

        Assert.True(await worker.OpenAsync(TestLines.Connection(), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken));
        string path = TestLines.ResultOf(
            await worker.RunAsync(TestLines.Command("path"), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken)).GetString()!;
        int pid = TestLines.ResultOf(
            await worker.RunAsync(TestLines.Command("pid"), cancellationToken).WaitAsync(TestTimes.Deadline, cancellationToken)).GetInt32();
        using Process process = Process.GetProcessById(pid);

        worker.Kill();
        // The synchronous wait: the asynchronous one keeps a handle of the process open for a moment after its exit,
        // which keeps its program file locked when the test's folder is removed.
        Assert.True(process.WaitForExit(TestTimes.Deadline));

        return path;
    }
}
