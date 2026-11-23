using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// install [--stop-timeout <TIME>]: elevation and the source, the stop, the program folder judged by the agent's rule
// before any service exists, the service, the two rights of its account, and the data folder, each step idempotent and
// named in its failure, as the S10 brief says.
internal sealed class InstallCommand : AsyncCommand<ServiceSettings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public InstallCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override Task<int> ExecuteAsync(CommandContext context, ServiceSettings settings, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
