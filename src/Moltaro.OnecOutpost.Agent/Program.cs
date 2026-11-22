using System.Reflection;
using System.Runtime.Versioning;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Host;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Start;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Agent.Workers;

namespace Moltaro.OnecOutpost.Agent;

// The agent program. It takes one argument, service, and runs as the Windows service, with the machine's data folder,
// access lists and DPAPI, the program's launcher of the COM workers, OutpostExecutor.epf beside the program, the handler
// of ServerRoute and the system's clock. It exits with AgentExitCodes: NotWindows elsewhere, which keeps the assembly
// testable on every platform; Usage for any other arguments, so that a run from a console never waits on the service
// controller; and otherwise what the service gives.
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
            new WindowsAccessLists(data),
            new MachineDpapi(),
            new ComWorkerLauncher(),
            Path.Combine(AppContext.BaseDirectory, "OutpostExecutor.epf"),
            (server, proxy) => ServerRoute.CreateHandler(server, proxy),
            TimeProvider.System,
            Version());
    }

    // The version of the build, as the polls carry it: the informational version that the build gives the assembly,
    // read once at the start.
    private static string Version()
    {
        return typeof(Program).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()!.InformationalVersion;
    }
}
