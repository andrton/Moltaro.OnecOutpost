using Spectre.Console;
using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// check [--clock-skew <TIME>]: elevation; the data folder, absent giving NotInstalled; then the lines of CheckLineEnum,
// each passed or failed with its reason, shown as one table in that order, and Success only when every line passed,
// else CheckFailed. It writes nothing to a base or to the data folder, runs only Reads of its own scripts, never polls,
// and works while the service runs.
internal sealed partial class CheckCommand : AsyncCommand<CheckCommand.Settings>
{
    // The console of the lines and the parts of the machine that the implementation reads.
    private readonly IAnsiConsole _console;

    private readonly ConsoleParts _parts;

    public CheckCommand(IAnsiConsole console, ConsoleParts parts)
    {
        _console = console;
        _parts = parts;
    }

    public override Task<int> ExecuteAsync(CommandContext context, Settings settings, CancellationToken cancellationToken)
    {
        throw new NotSupportedException("check is S10b's implementation.");
    }
}
