using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// pair <SERVER> <TOKEN>: the steps it shares with secret, then the pairing of §6.1 of docs/protocol.md, writing
// secrets.dat with the new key alone and then agent.json, as the S10 brief says.
internal sealed partial class PairCommand : AsyncCommand<PairCommand.Settings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public PairCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
