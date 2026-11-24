using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// The commands of the console and how a run of them ends, for Program and for every test that runs them.
internal static class ConsoleApp
{
    // Registers the parts for the commands, parses strictly, and gives the exceptions that reach Spectre their codes:
    // its own code for a CommandEndException, Cancelled for an OperationCanceledException whose token is cancelled,
    // Usage for Spectre's parse and validation errors, Failure for any other, each written in red as
    // {Kind} ({code}): {reason}.
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
        config.AddCommand<CheckCommand>("check")
            .WithDescription("Checks the agent machine: the connector, the bases, the server, the worker accounts and the access lists.");
        config.AddCommand<MetadataCommand>("metadata")
            .WithDescription("Writes a local report of a base's metadata: its objects, their fields and how they are stored.");
        config.AddBranch("connector", connector =>
        {
            connector.SetDescription("The COM connector of 1C:Enterprise.");

            // Spectre shows the help of a branch run without its command and ends with 1, the code of Failure; this
            // command makes that run the usage error that it is.
            connector.SetDefaultCommand<ConnectorCommand>();
            connector.AddCommand<ConnectorRegisterCommand>("register")
                .WithDescription("Registers the COM connector that is installed, for each bitness that has none registered.");
        });
    }

    private static int End(Exception exception, IAnsiConsole console)
    {
        (int code, string reason) = exception switch
        {
            CommandEndException end => (end.Code, end.Message),
            OperationCanceledException { CancellationToken.IsCancellationRequested: true } =>
                (ConsoleExitCodes.Cancelled, "the command was cancelled"),

            // A value that a converter of the console refused: its message names the rule, where Spectre's own quotes
            // the value given.
            CommandRuntimeException { InnerException: FormatException refused } => (ConsoleExitCodes.Usage, refused.Message),
            CommandParseException or CommandRuntimeException => (ConsoleExitCodes.Usage, exception.Message),
            _ => (ConsoleExitCodes.Failure, $"{exception.GetType().Name}: {exception.Message}"),
        };

        console.MarkupLineInterpolated($"[red]{KindOf(code)} ({code}): {reason}[/]");

        return code;
    }

    // The name that ConsoleExitCodes gives each code of an end other than Success.
    private static string KindOf(int code)
    {
        return code switch
        {
            ConsoleExitCodes.Failure => nameof(ConsoleExitCodes.Failure),
            ConsoleExitCodes.Usage => nameof(ConsoleExitCodes.Usage),
            ConsoleExitCodes.NotElevated => nameof(ConsoleExitCodes.NotElevated),
            ConsoleExitCodes.Cancelled => nameof(ConsoleExitCodes.Cancelled),
            ConsoleExitCodes.NotInstalled => nameof(ConsoleExitCodes.NotInstalled),
            ConsoleExitCodes.StopTimedOut => nameof(ConsoleExitCodes.StopTimedOut),
            ConsoleExitCodes.ProgramFolderWriters => nameof(ConsoleExitCodes.ProgramFolderWriters),
            ConsoleExitCodes.DataFolderAdmitsOthers => nameof(ConsoleExitCodes.DataFolderAdmitsOthers),
            ConsoleExitCodes.PolicyInvalid => nameof(ConsoleExitCodes.PolicyInvalid),
            ConsoleExitCodes.AlreadyPaired => nameof(ConsoleExitCodes.AlreadyPaired),
            ConsoleExitCodes.PairingFailed => nameof(ConsoleExitCodes.PairingFailed),
            ConsoleExitCodes.NotPaired => nameof(ConsoleExitCodes.NotPaired),
            ConsoleExitCodes.NotInPolicy => nameof(ConsoleExitCodes.NotInPolicy),
            ConsoleExitCodes.UnknownAccount => nameof(ConsoleExitCodes.UnknownAccount),
            ConsoleExitCodes.NotInteractive => nameof(ConsoleExitCodes.NotInteractive),
            ConsoleExitCodes.ConnectorNotRegistered => nameof(ConsoleExitCodes.ConnectorNotRegistered),
            ConsoleExitCodes.RegistrationFailed => nameof(ConsoleExitCodes.RegistrationFailed),
            ConsoleExitCodes.CheckFailed => nameof(ConsoleExitCodes.CheckFailed),
            ConsoleExitCodes.ReportFailed => nameof(ConsoleExitCodes.ReportFailed),
            _ => throw new ArgumentOutOfRangeException(nameof(code), code, "No end of a command has this code."),
        };
    }
}
