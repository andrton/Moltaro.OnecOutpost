using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Console.Commands;

// connector without its command: a usage error, as a missing argument is. It is the branch's default command, which
// runs only when no command of the branch is given.
internal sealed class ConnectorCommand : Command<EmptyCommandSettings>
{
    public override int Execute(CommandContext context, EmptyCommandSettings settings, CancellationToken cancellationToken)
    {
        throw new CommandEndException(ConsoleExitCodes.Usage, "connector needs its command, connector register; see connector --help");
    }
}
