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
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console;

// The console of the agent machine. It gives NotWindows elsewhere, and otherwise runs the commands on Spectre's own
// console, whose detection decides colour, animation and prompts, with the parts of the machine. Ctrl+C cancels the
// token of the run, Cancel being set so that the process is not ended at once; each command checks the token before
// each of its steps. Its output, when redirected, is for a script: UTF-8, whatever the code page of the console, and
// each line whole, never wrapped at the width that Spectre assumes without a terminal.
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

            ConsoleParts parts = MachineParts(
                ServiceName,
                AppContext.BaseDirectory,
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), FolderName),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), FolderName));

            IAnsiConsole console = MachineConsole();

            app.Configure(config =>
            {
                ConsoleApp.Configure(config, parts);
                config.ConfigureConsole(console);
            });

            return await app.RunAsync(arguments, cancellation.Token);
        }
        finally
        {
            System.Console.CancelKeyPress -= Cancel;
        }
    }

    // The parts of the machine for the service of this name, the folder the console runs from, the program folder and
    // the data folder: Main gives those of the agent, and the test of install on Windows those of a service of its own.
    [SupportedOSPlatform("windows")]
    internal static ConsoleParts MachineParts(string serviceName, string source, string programFolder, string dataFolder)
    {
        return new ConsoleParts(
            IsElevated(),
            serviceName,
            source,
            programFolder,
            dataFolder,
            new DiskFolder(dataFolder),
            new WindowsServiceControl(TimeProvider.System),
            new WindowsAccountRights(),
            new WindowsFolderSetup(),
            sid => new WindowsAccessLists(dataFolder, new SecurityIdentifier(sid)),
            new MachineDpapi(),
            ConnectorFinder.Machine(),
            new RegSvr32Registration(),
            (server, proxy) => ServerRoute.CreateHandler(server, proxy),
            RedirectedInput(),
            TimeProvider.System,
            Version());
    }

    // Spectre's console on the standard output. A redirected output gets a writer of UTF-8 in the place of the one of
    // the console's code page, which loses every character the code page lacks, such as §; the writer stays
    // Console.Out, so that Spectre still sees the output as the redirected standard output. The code page of the
    // console itself is not changed, since a console that the shell shares would keep it after the run. Spectre's
    // default enrichers are off: those of CI systems, such as GitHub Actions by GITHUB_ACTIONS and Azure Pipelines by
    // TF_BUILD, would turn colour on whatever the output is, so that only Spectre's own detection decides, and an output
    // that is not an interactive terminal gets neither colour nor animation.
    private static IAnsiConsole MachineConsole()
    {
        bool redirected = System.Console.IsOutputRedirected;

        if (redirected)
        {
            StreamWriter output = new(System.Console.OpenStandardOutput(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false))
            {
                AutoFlush = true,
            };

            System.Console.SetOut(output);
        }

        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });

        if (redirected)
        {
            console.Profile.Width = int.MaxValue;
        }

        return console;
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
