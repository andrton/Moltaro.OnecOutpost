using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// The commands of the console and how a run of them ends, for Program and for every test that runs them.
internal static class ConsoleApp
{
    // Registers the parts for the commands, parses strictly, and gives the exceptions that reach Spectre their codes:
    // Cancelled for an OperationCanceledException whose token is cancelled, Usage for Spectre's parse and validation
    // errors, Failure for any other, each written in red as {Kind} ({code}): {reason}.
    public static void Configure(IConfigurator config, ConsoleParts parts)
    {
        config.Settings.Registrar.RegisterInstance(parts);
        config.SetApplicationName("Moltaro.OnecOutpost.Console");
        config.UseStrictParsing();
        config.SetExceptionHandler((exception, _) => End(exception, config.Settings.Console ?? AnsiConsole.Console));

        config.AddCommand<InstallCommand>("install")
            .WithDescription("Installs the agent's service, its program folder and its data folder, or updates them.");
        config.AddCommand<UninstallCommand>("uninstall")
            .WithDescription("Stops and removes the agent's service; both folders stay.");
        config.AddCommand<PairCommand>("pair")
            .WithDescription("Pairs the agent with the server by a pairing token.");
        config.AddCommand<SecretCommand>("secret")
            .WithDescription("Writes a password of a base to secrets.dat.");
        config.AddBranch("connector", connector =>
        {
            connector.SetDescription("The COM connector of 1C:Enterprise.");
            connector.AddCommand<ConnectorRegisterCommand>("register")
                .WithDescription("Registers the COM connector that is installed, for each bitness that has none registered.");
        });
    }

    private static int End(Exception exception, IAnsiConsole console)
    {
        (string kind, int code, string reason) = exception switch
        {
            OperationCanceledException { CancellationToken.IsCancellationRequested: true } =>
                (nameof(ConsoleExitCodes.Cancelled), ConsoleExitCodes.Cancelled, "the command was cancelled"),
            CommandParseException or CommandRuntimeException =>
                (nameof(ConsoleExitCodes.Usage), ConsoleExitCodes.Usage, exception.Message),
            _ =>
                (nameof(ConsoleExitCodes.Failure), ConsoleExitCodes.Failure, $"{exception.GetType().Name}: {exception.Message}"),
        };

        console.MarkupLineInterpolated($"[red]{kind} ({code}): {reason}[/]");

        return code;
    }
}
