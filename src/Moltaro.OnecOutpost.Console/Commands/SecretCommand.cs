using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// secret <BASE> <KIND>: the steps it shares with pair, then the password of the base's account in the mode, or of its
// worker account after "Log on as a batch job" is granted to it, written to secrets.dat with every other secret kept,
// as the S10 brief says.
internal sealed partial class SecretCommand : AsyncCommand<SecretCommand.Settings>
{
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public SecretCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        throw new NotSupportedException();
    }
}
