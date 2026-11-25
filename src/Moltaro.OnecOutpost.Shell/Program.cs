using System.Text;
using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

// The shell's public entry point is the executable. It opens one server and reads commands from standard input.
internal static class Program
{
    private static async Task<int> Main(string[] arguments)
    {
        ShellOutput output = new(MachineConsole());
        using CancellationTokenSource cancellation = new();

        void Cancel(object? sender, ConsoleCancelEventArgs interrupt)
        {
            interrupt.Cancel = true;
            cancellation.Cancel();
        }

        Console.CancelKeyPress += Cancel;

        try
        {
            CommandApp<StartCommand> app = new();
            app.Configure(config =>
            {
                output.ConfigureCli(config, cancellation.Token);
                config.Settings.Registrar.RegisterInstance(output);
                config.SetApplicationName("Moltaro.OnecOutpost.Shell");
            });

            return await app.RunAsync(arguments, cancellation.Token);
        }
        finally
        {
            Console.CancelKeyPress -= Cancel;
        }
    }

    private static IAnsiConsole MachineConsole()
    {
        bool redirected = Console.IsOutputRedirected;

        if (redirected)
        {
            // A pipe gets UTF-8 regardless of the console's code page. Spectre still sees it as standard output.
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true });
        }
        else if (OperatingSystem.IsWindows())
        {
            // This selects WriteConsoleW without changing the code page of the terminal hosting the process.
            Console.OutputEncoding = Encoding.Unicode;
        }

        IAnsiConsole console = AnsiConsole.Create(new AnsiConsoleSettings
        {
            Ansi = redirected ? AnsiSupport.No : AnsiSupport.Detect,
            ColorSystem = redirected ? ColorSystemSupport.NoColors : ColorSystemSupport.Detect,
            Interactive = redirected ? InteractionSupport.No : InteractionSupport.Detect,
            Enrichment = new ProfileEnrichment { UseDefaultEnrichers = false },
        });

        if (redirected)
        {
            console.Profile.Width = int.MaxValue;
        }

        return console;
    }
}
