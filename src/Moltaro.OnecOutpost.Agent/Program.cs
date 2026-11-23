using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent;

// The agent program. It takes one argument, service, and runs as the Windows service, with the machine's data folder,
// access lists for its own account and DPAPI, the program's launcher of the COM workers, AppContext.BaseDirectory as the program folder,
// OutpostExecutor.epf beside the program, the handler of ServerRoute and the system's clock. The launcher starts the
// worker in x64\ or x86\ beside the program, in one job that the program never disposes, so that Windows closes it,
// and ends the workers, when the agent ends. It exits with AgentExitCodes: NotWindows elsewhere, which keeps the
// assembly testable on every platform; Usage for any other arguments, so that a run from a console never waits on the
// service controller; and otherwise what the service gives.
internal static class Program
{
    private const string ServiceArgument = "service";

    private static int Main(string[] arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return AgentExitCodes.NotWindows;
        }

        if (arguments is not [ServiceArgument])
        {
            return AgentExitCodes.Usage;
        }

        return RunService();
    }

    // The parts are made once the service controller has started the service, so that a process it did not start
    // touches nothing of the data folder.
    [SupportedOSPlatform("windows")]
    private static int RunService()
    {
        return AgentService.Run(cancellationToken => AgentHost.RunAsync(MachineParts(), cancellationToken));
    }

    [SupportedOSPlatform("windows")]
    private static AgentParts MachineParts()
    {
        string data = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            "Moltaro.OnecOutpost");

        AgentFolders folders = new(
            new DiskFolder(data),
            new DiskFolder(Path.Combine(data, "audit")),
            new DiskFolder(Path.Combine(data, "outcomes")));

        return new AgentParts(
            folders,
            new WindowsAccessLists(data, AgentAccount()),
            new MachineDpapi(),
            new ComWorkerLauncher(
                Path.Combine(AppContext.BaseDirectory, "x64", ComWorkerLauncher.WorkerFile),
                Path.Combine(AppContext.BaseDirectory, "x86", ComWorkerLauncher.WorkerFile),
                ConnectorFinder.Machine(),
                new WorkerJob()),
            AppContext.BaseDirectory,
            Path.Combine(AppContext.BaseDirectory, "OutpostExecutor.epf"),
            (server, proxy) => ServerRoute.CreateHandler(server, proxy),
            TimeProvider.System,
            Version());
    }

    // The account the agent runs under, NT SERVICE\MoltaroOnecOutpost as a service, which its access lists admit.
    [SupportedOSPlatform("windows")]
    private static SecurityIdentifier AgentAccount()
    {
        using WindowsIdentity agent = WindowsIdentity.GetCurrent();

        return agent.User!;
    }

    // The version of the build, as the polls carry it: the product version of the program's file, which the build takes
    // from the informational version of the assembly, read once at the start. The service controller starts the
    // program's own executable, so that is the file of the process.
    private static string Version()
    {
        return FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).ProductVersion!;
    }
}
