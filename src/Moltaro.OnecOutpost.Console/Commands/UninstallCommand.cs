using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// uninstall [--stop-timeout <TIME>]: elevation, the stop, the two rights taken from the service's account, and the
// service deleted; both folders and the batch right of the worker accounts stay, as the S10 brief says.
internal sealed class UninstallCommand : AsyncCommand<ServiceSettings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public UninstallCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override Task<int> ExecuteAsync(CommandContext context, ServiceSettings settings, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
