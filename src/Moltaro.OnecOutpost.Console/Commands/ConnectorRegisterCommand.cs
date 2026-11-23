using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// connector register: elevation, then for each bitness without a registered connector the one copy installed is
// registered by the regsvr32 of its bitness; several or none are lines of the output, as the S10 brief says.
internal sealed class ConnectorRegisterCommand : AsyncCommand<EmptyCommandSettings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public ConnectorRegisterCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override Task<int> ExecuteAsync(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
