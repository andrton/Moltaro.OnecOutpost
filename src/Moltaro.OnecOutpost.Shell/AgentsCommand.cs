using Spectre.Console.Cli;

namespace Moltaro.OnecOutpost.Shell;

internal sealed class AgentsCommand(ShellSession session) : AsyncCommand
{
    public override async Task<int> ExecuteAsync(CommandContext context, CancellationToken cancellationToken)
    {
        session.Output.Agents(await session.CallAsync("Listing agents", () => session.Server.GetAgentsAsync(cancellationToken)));
        return ShellExit.Success;
    }
}
