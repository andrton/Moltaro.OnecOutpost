using System.Diagnostics;
using System.Runtime.Versioning;
using System.Security.Principal;
using System.Text;
using Moltaro.OnecOutpost.Agent.Access;
using Moltaro.OnecOutpost.Agent.Connector;
using Moltaro.OnecOutpost.Agent.Files;
using Moltaro.OnecOutpost.Agent.Secrets;
using Moltaro.OnecOutpost.Agent.Transport;
using Moltaro.OnecOutpost.Console.Commands;
using Moltaro.OnecOutpost.Console.Connector;
using Moltaro.OnecOutpost.Console.Folders;
using Moltaro.OnecOutpost.Console.Rights;
using Moltaro.OnecOutpost.Console.Service;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console;

// The console of the agent machine. It gives NotWindows elsewhere, and otherwise runs the commands on Spectre's own
// console, whose detection decides colour, animation and prompts, with the parts of the machine. Ctrl+C cancels the
// token of the run, Cancel being set so that the process is not ended at once; each command checks the token before
// each of its steps.
internal static class Program
{
    // The service of the agent, whose virtual account is NT SERVICE\MoltaroOnecOutpost.
    private const string ServiceName = "MoltaroOnecOutpost";

    // The folder of the program under %ProgramFiles% and of the data under %ProgramData%.
    private const string FolderName = "Moltaro.OnecOutpost";

    private static async Task<int> Main(string[] arguments)
    {
        if (!OperatingSystem.IsWindows())
        {
            return ConsoleExitCodes.NotWindows;
        }

        return await RunAsync(arguments);
    }

    [SupportedOSPlatform("windows")]
    private static async Task<int> RunAsync(string[] arguments)
    {
        using CancellationTokenSource cancellation = new();

        void Cancel(object? sender, ConsoleCancelEventArgs press)
        {
            press.Cancel = true;
            cancellation.Cancel();
        }

        System.Console.CancelKeyPress += Cancel;

        try
        {
            CommandApp app = new();
            ConsoleParts parts = MachineParts();

            app.Configure(config => ConsoleApp.Configure(config, parts));

            return await app.RunAsync(arguments, cancellation.Token);
        }
        finally
        {
            System.Console.CancelKeyPress -= Cancel;
        }
    }

    [SupportedOSPlatform("windows")]
    private static ConsoleParts MachineParts()
    {
        string data = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), FolderName);

        return new ConsoleParts(
            IsElevated(),
            ServiceName,
            AppContext.BaseDirectory,
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FolderName),
            data,
            new DiskFolder(data),
            new WindowsServiceControl(TimeProvider.System),
            new WindowsAccountRights(),
            new WindowsFolderSetup(),
            sid => new WindowsAccessLists(data, new SecurityIdentifier(sid)),
            new MachineDpapi(),
            ConnectorFinder.Machine(),
            new RegSvr32Registration(),
            (server, proxy) => ServerRoute.CreateHandler(server, proxy),
            RedirectedInput(),
            TimeProvider.System,
            Version());
    }

    // True for an elevated token only: UAC's filtered token holds Administrators as deny-only.
    [SupportedOSPlatform("windows")]
    private static bool IsElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();

        return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
    }

    // A redirected standard input, such as a pipe or a file, read as UTF-8 whatever the console's code page, a byte
    // order mark skipped; null when the input is the console.
    private static TextReader? RedirectedInput()
    {
        if (!System.Console.IsInputRedirected)
        {
            return null;
        }

        return new StreamReader(System.Console.OpenStandardInput(), Encoding.UTF8, detectEncodingFromByteOrderMarks: false);
    }

    // The version of the build, as the agent reads its own: the product version of the program's file.
    private static string Version()
    {
        return FileVersionInfo.GetVersionInfo(Environment.ProcessPath!).ProductVersion!;
    }
}
